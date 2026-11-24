#if !PRODUCTION_BUILD
// #define DEBUG_MINDS
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.BodyParts;
using Game.Systems.Stats;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Items;
using Game.Systems.Mental;
using Game.Systems.Backstories;
using Game.Systems.Traits;
using Game.Systems.Sleep;
using Game.Systems.Naming;
using Game.Systems.UIUX;
using Game.Systems.UIUX.Codex.Pages;
using Game.Systems.Drama;
using System;
using Game.Utils;
using KL.Grid;
using Game.Procgen;
using System.Linq;
using Game.Systems.Creatures.Surgeries;
using Game.Input;
using Game.UI;

namespace Game.Systems.Creatures {
    public sealed class BeingsSys : GameSystem, ISaveableSpecial {
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new BeingsSys());
            if (avatars.Count != 0) {
                foreach (var av in avatars) {
                    D.Warn("Unloading stale avatar texture: {0}", av.Value.Tex);
                    GameObject.DestroyImmediate(av.Value.Tex);
                }
                avatars.Clear();
            }
        }
        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;
        public const string SysId = "BeingsSys";
        public override string Id => SysId;
        public readonly List<BeingData> ActiveBeings = new();
        public readonly Dictionary<uint, BeingData> All = new();
        public readonly Dictionary<uint, Mind> Minds = new();
        private readonly Dictionary<Def, BInt> beingCountsB = new();
        private DirtSys dirtSys;
        private SurgicalQueue surgicalQueue;
        private Curve maxLibidoByAgeCurve;
        private bool needsBeingSitRebuild;
        private readonly List<BeingGroup> beingGroups = new();
        private static readonly Dictionary<BeingData, BeingAvatar> avatars = new();
        private int opinionSweepCursor;
        private readonly List<Mind> opinionSweepScratch = new();
        /// <summary>
        /// Beings currently suspected of being orphaned with a stale InDevice state
        /// (see <see cref="CheckDeviceOrphan"/>), mapped to the tick they were first
        /// suspected. Requiring the condition to hold across two checks avoids false
        /// positives from a being momentarily off its device tile mid-transition.
        /// </summary>
        private readonly Dictionary<BeingData, long> suspectedDeviceOrphans = new();
        private readonly ErrorThrottle tickErrors = new();
        /// <summary>
        /// Set once a rescued guest has been noticed aboard the player's ship, so the
        /// GuestsPage tutorial popup is only ever asked for once per session (see OnTick).
        /// </summary>
        private bool noticedGuestRescue;

        /// <summary>
        /// Runs once per in-game day. Advances a rolling cursor over the Minds registry
        /// and forgets stale acquaintance opinions for a slice of minds each day, so the
        /// full dictionary is swept roughly once per month.
        /// </summary>
        private void SweepStaleOpinions(long ticks) {
            if (ticks % Consts.TicksPerDay != 31) { return; }
            if (Minds.Count == 0) { return; }
            long staleBefore = ticks - Tunables.ForgetAcquaintancesAfterDays * Consts.TicksPerDay;
            opinionSweepScratch.Clear();
            opinionSweepScratch.AddRange(Minds.Values);
            int n = opinionSweepScratch.Count;
            int slice = Mathf.CeilToInt(n / 30f); // full pass every ~30 days (one month)
            for (int i = 0; i < slice; i++) {
                if (opinionSweepCursor >= n) { opinionSweepCursor = 0; }
                opinionSweepScratch[opinionSweepCursor++].Bonds
                    ?.ForgetStaleAcquaintances(staleBefore);
            }
        }

        private void OnTick(long ticks) {
            SweepStaleOpinions(ticks);
            bool checkDirt = ticks % Consts.TicksPer30Minutes == 12;
            bool checkDeviceOrphans = ticks % Consts.TicksPer10Minutes == 3;
            bool checkGuestRescue = !noticedGuestRescue && ticks % Consts.TicksPer10Minutes == 7;
            bool hadSuspectedOrphans = suspectedDeviceOrphans.Count > 0;
            bool groupsChanged = false;
            bool isMonth = ticks % Consts.TicksPerMonth == 0;
            BeingData employeeOTM = null;
            int workRecordMonth = 0;
            foreach (var b in All.Values) {
                // Frozen in a stasis pod: no ageing, no group or counter upkeep. Ahead of
                // everything else so a full manifest costs one compare per sleeper per tick.
                if (b.Mind?.Role == ColonyRole.InStasis) { continue; }
                // one being that threw on every tick stopped every being after it from
                // ticking (ageing, groups, monthly counters)
                try {
                    b.Tick(S, ticks);
                    var prevGroupId = b.GroupId;
                    BeingGroup.AssignFor(b);
                    if (b.GroupId != prevGroupId) {
                        groupsChanged = true;
                    }
                    if (checkDirt && !b.HasCondition(ConditionIdH.Sleeping)
                            && !b.Body.IsIncapacitated) {
                        CheckDirt(b, ticks);
                    }
                    if (b.HasStateFlag(BeingState.InDevice)) {
                        if (checkDeviceOrphans) {
                            CheckDeviceOrphan(b, ticks);
                        }
                    } else if (hadSuspectedOrphans && suspectedDeviceOrphans.Count > 0) {
                        suspectedDeviceOrphans.Remove(b);
                    }
                    if (checkGuestRescue && PassengerUtils.IsGuestAboard(b)
                            && !S.Query.IsMindRequiredByQuest.Ask(b.Mind.Id)) {
                        // First rescued (not merely spawned) guest of the session - point the
                        // player at the repatriation loop they'd otherwise have no way to find.
                        noticedGuestRescue = true;
                        S.UIQuery.ShowTutorialPopup.Ask(GuestsPage.PageId, 5f);
                    }
                    if (isMonth) {
                        var worked = b.Counters.Get(CounterIdH.TicksWorkedMonth);
                        var idle = b.Counters.Get(CounterIdH.TicksIdleMonth);
                        var moving = b.Counters.Get(CounterIdH.TicksMovingMonth);
                        b.Counters.Set(CounterIdH.TicksWorkedLastMonth, worked);
                        b.Counters.Set(CounterIdH.TicksIdleLastMonth, idle);
                        b.Counters.Set(CounterIdH.TicksMovingLastMonth, moving);
                        b.Counters.Set(CounterIdH.TicksWorkedMonth, 0);
                        b.Counters.Set(CounterIdH.TicksIdleMonth, 0);
                        b.Counters.Set(CounterIdH.TicksMovingMonth, 0);
                        if (worked > workRecordMonth) {
                            if (b.Mind.Health != null && b.HasStateFlag(BeingState.Alive)
                                    && b.IsPlayer) {
                                workRecordMonth = worked;
                                employeeOTM = b;
                            }
                        }
                    }
                } catch (Exception ex) {
                    if (tickErrors.ShouldLog(b, out var count)) {
                        D.LogEx(ex, "Failed ticking being {0} ({1} times)", b, count);
                    }
                }
            }
            // the throttle holds its keys, so gone beings are let go daily
            if (ticks % Consts.TicksPerDay == 0) {
                tickErrors.Clear();
            }
            if (isMonth && employeeOTM != null) {
                BeingUtils.NoticeWorkerOfTheMonth(S, employeeOTM, ticks);
            }

            if (groupsChanged) {
                S.Sig.RebuildBeingGroups.Send();
            }

            #if !PRODUCTION_BUILD
            for (int i = S.Beings.Alive.Count - 1; i >= 0; i--) {
                Being b = S.Beings.Alive[i];
                if (!b.IsActive || !b.IsAlive) {
                    D.Err("Inactive / dead being in Beings.Alive list! {0}", b);
                    S.Beings.Alive.RemoveAt(i);
                }
            }
            #endif

            surgicalQueue.Tick(ticks);

            if (needsBeingSitRebuild) {
                RebuildBeingsSituation();
            }
        }

        private void CheckDirt(BeingData b, long ticks) {
            if (b.Being == null || b.Mind.Health == null) { return; }
            // hidden or dead won't check dirt
            if ((b.StateFlags & (BeingState.Dead | BeingState.Hidden))
                    != BeingState.Uninitialized) {
                return;
            }
            if (!b.Species.IsHumanoid) { return; }
            var dirtCnt = dirtSys.CountDirtNearby(b.Being.PosIdx, 3);
            float ignoreChance;
            switch (dirtCnt) {
                case 0: return;
                case 1: ignoreChance = 0.95f; break;
                case 2: ignoreChance = 0.9f; break;
                default: ignoreChance = 0.8f; break;
            }
            if (Rng.UChance(ignoreChance)) { return; }
            var eff = MentalEffect.CreateWithIcon(
                b.Being.PosIdx,
                IconId.CNoSparkles, ticks, Consts.TicksPerHour,
                    T.Dirt, -dirtCnt);
            S.Sig.AddMentalEffect.Send(b, eff);
        }

        private BeingAvatar GetMindAvatar(Mind mind) {
            if (mind.Owner != null) {
                return GetAvatar(mind.Owner);
            }
            if (mind.FormerOwnerId > 0) {
                if (All.TryGetValue(mind.FormerOwnerId, out var fOwner)) {
                    return GetAvatar(fOwner);
                }
            }
            return null;
        }
        private BeingAvatar GetAvatar(BeingData data) {
            if (!avatars.TryGetValue(data, out var avatar)) {
                avatar = BeingAvatar.CreateFor(data);
                data.Looks.Avatar = avatar;
                avatars[data] = avatar;
            }
            return avatar;
        }

        public override void Unload() {
            foreach (var avatar in avatars.Values) {
                avatar.Cleanup();
            }
            foreach (var b in All.Values) {
                b.Looks?.SurgeryPreview?.Cleanup();
            }
            storage = null;
            avatars.Clear();
        }

        protected override void OnInitialize() {
            Mind.LastId = 0;
            BeingData.LastId = 0;
            surgicalQueue = new SurgicalQueue(S);
            maxLibidoByAgeCurve = CurveDef.FastGet(CurveIdH.Drama_MaxLibidoByAge);

            S.Sig.AfterLoadState.AddListener(OnLoadState);
            S.Sig.BeingDied.AddListener(OnBeingDeath);
            S.Sig.BeingDied.AddListener(b => UpdateBeingCountB(b?.Species?.Def));
            S.Sig.BeingAdded.AddListener(b => UpdateBeingCountB(b?.Definition));
            S.Sig.MindInstalled.AddListener(OnMindInstalled);
            S.UIQuery.GetBindableBeingCountFor.SetResponder(GetBindableBeingCount);

            S.Query.FindMind.SetResponder(FindMind);
            S.Query.RegisterMind.SetResponder(RegisterMind);
            S.Query.GenBeingFull.SetResponder(GenerateBeing);
            S.Query.GenBeingPart.SetResponder(GenerateMissingParts);
            S.Query.GenBabyMind.SetResponder(GenBabyMind);
            S.Query.GetBeingAvatar.SetResponder(GetAvatar);
            S.Query.GetMindAvatar.SetResponder(GetMindAvatar);
            S.Query.ActivateBeingData.SetResponder(Activate);
            S.Query.DeactivateBeingData.SetResponder(Deactivate);
            S.Query.RegisterDormantBeing.SetResponder(RegisterDormant);
            S.Query.AllBeingData.SetResponder(GetAll);
            S.Query.GetOpinionAbout.SetResponder(GetOpinionAbout);
            S.Query.RollSexualAttraction.SetResponder(GenerateSexualAttractionFor);
            S.Query.RollSocialAffinity.SetResponder(GenerateSocialCompatibilityFor);
            S.Query.CountFriendlyBeingsBySpecies.SetResponder(CountFriendlyByDef);
            S.Query.LoadBeingsForGroup.SetResponder(LoadBeingsForGroup);
            S.Query.GetBeingGroups.SetResponder(GetBeingGroups);
        }

        private BInt GetBindableBeingCount(Def def) {
            if (!beingCountsB.TryGetValue(def, out var bInt)) {
                bInt = new BInt(S.Beings.CountByDef(def), capacity: 64);
                beingCountsB[def] = bInt;
            }
            return bInt;
        }

        private void UpdateBeingCountB(Def def) {
            if (def == null) { return; }
            if (beingCountsB.TryGetValue(def, out var bInt)) {
                bInt.Set(S.Beings.CountByDef(def));
            }
        }

        private void OnBeingDeath(BeingData dead) {
            if (dead.Mind.Health == null) { return; }
            uint deadMindId = dead.Mind.Id;
            foreach (var being in All.Values) {
                if (being.Mind.Bonds?.Opinions == null || being.HasStateFlag(BeingState.Dead)) {
                    continue;
                }
                if (!being.Mind.Bonds.Opinions.TryGetValue(deadMindId, out var op)) { continue; }
                ConsiderGriefing(being, dead, op);
            }
            if (dead.Being != null && S.Query.IsSelected.Ask(dead.Being)) {
                UIUtils.RefreshSelectedEntityUI();
            }
        }

        // A colonist has three friends and two crushes aboard on average, so one death reaches
        // about five minds. At twice these values each of them sank into the heavy breakdown
        // band and one death set off four heavy breakdowns
        // (Docs/Architecture/Plans/SanityBalance.md).
        private void ConsiderGriefing(BeingData being, BeingData dead, BeingOpinion op) {
            short change = 0;
            int duration = 0;
            string status = null;
            var rng = Rng.Unseeded;
            if (op.Flags.HasFlag(BeingOpinionFlags.Lover)) {
                change = -15;
                duration = (int) (rng.Range(5f, 10f) * Consts.TicksPerDay);
                status = T.NounLover;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Friend)) {
                change = -10;
                duration = (int) (rng.Range(4f, 8f) * Consts.TicksPerDay);
                status = T.NounFriend;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Nemesis)) {
                change = +5;
                duration = (int) (rng.Range(2f, 4f) * Consts.TicksPerDay);
                status = T.StatusNemesis;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Master)) {
                change = -15;
                status = T.StatusMaster;
                duration = (int) (rng.Range(6f, 10f) * Consts.TicksPerDay);
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Pet)) {
                change = -8;
                duration = (int) (rng.Range(3f, 5f) * Consts.TicksPerDay);
                status = T.Pet;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Crush)) {
                change = -10;
                duration = (int) (rng.Range(3f, 5f) * Consts.TicksPerDay);
                status = T.StatusCrush;
            }
            if (change == 0) {
                return;
            }
            var title = T.EvtDeathTitle_V1.F(dead.Mind.LinkToShortName);
            title = $"{title} ({Texts.Accented(status, false)})";
            being.Mind.Health.AddEffect(S,
                MentalEffect.CreateWithIcon(dead.Being?.PosIdx ?? Pos.Invalid,
                    IconId.CSkull, G.Ticks, duration, title, change));
        }

        private void OnMindInstalled(Mind installedMind, ObjData partData, BeingData newOwner) {
            // Check if this is a resurrection - mind had died before
            if (installedMind.LastTimeOfDeath == 0) {
                return; // Mind never died, not a resurrection
            }

            // Mind was previously dead, now it's being resurrected
            if (installedMind.Health == null) { return; }

            foreach (var being in All.Values) {
                if (being.Mind.Bonds?.Opinions == null || being.HasStateFlag(BeingState.Dead)) {
                    continue;
                }
                foreach (var op in being.Mind.Bonds.Opinions) {
                    if (op.Key != installedMind.Id) { continue; }
                    ConsiderRejoicing(being, newOwner, op.Value);
                }
            }
        }

        private void ConsiderRejoicing(BeingData being, BeingData resurrected, BeingOpinion op) {
            short change = 0;
            int duration = 0;
            string status = null;
            var rng = Rng.Unseeded;
            if (op.Flags.HasFlag(BeingOpinionFlags.Lover)) {
                change = +30;
                duration = (int) (rng.Range(3f, 7f) * Consts.TicksPerDay);
                status = T.NounLover;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Friend)) {
                change = +20;
                duration = (int) (rng.Range(2f, 5f) * Consts.TicksPerDay);
                status = T.NounFriend;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Nemesis)) {
                change = -10;
                duration = (int) (rng.Range(1f, 2f) * Consts.TicksPerDay);
                status = T.StatusNemesis;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Master)) {
                change = +30;
                status = T.StatusMaster;
                duration = (int) (rng.Range(4f, 7f) * Consts.TicksPerDay);
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Pet)) {
                change = +15;
                duration = (int) (rng.Range(2f, 3f) * Consts.TicksPerDay);
                status = T.Pet;
            } else if (op.Flags.HasFlag(BeingOpinionFlags.Crush)) {
                change = +20;
                duration = (int) (rng.Range(1f, 3f) * Consts.TicksPerDay);
                status = T.StatusCrush;
            }
            if (change == 0) {
                return;
            }
            var title = $"{resurrected.Mind.LinkToShortName} {GlyphMap.ArrowRight} {T.BeingStateAlive}!";
            title = $"{title} ({Texts.Accented(status, false)})";
            being.Mind.Health.AddEffect(S,
                MentalEffect.CreateWithIcon(resurrected.Being?.PosIdx ?? Pos.Invalid,
                    IconId.CHeart, G.Ticks, duration, title, change));
        }

        private BeingOpinion GetOpinionAbout(Mind m1, Mind m2) {
            if (m1 == null || m2 == null) {
                D.Err("Trying to get an opinion of null mind: {0}->{1}", m1, m2);
                return null;
            }
            if (m1 == m2) {
                D.Err("Trying to get an opinion about themselves: {0}", m1);
                return null;
            }
            if (m1.Id == 0) { RegisterMind(m1); }
            if (m2.Id == 0) { RegisterMind(m2); }
            BeingBonds.EnsureFor(m1);
            if (!m1.Bonds.Opinions.TryGetValue(m2.Id, out var opinion)) {
                var rng = S.Rng.Fork();
                opinion = new BeingOpinion();
                opinion.SexualAttraction = GenerateSexualAttractionFor(m1.Owner, m2.Owner, rng);
                opinion.SocialAffinity = GenerateSocialCompatibilityFor(m1, m2, rng);
                opinion.OwnerMindId = m1.Id;
                opinion.TargetMind = m2;
                opinion.MarkChanged(); // stamp creation so newly-created opinions aren't instantly forgotten
                m1.Bonds.Opinions[m2.Id] = opinion;
            }
            return opinion;
        }

        private float GenerateSocialCompatibilityFor(Mind m1, Mind m2, Rng rng) {
            byte level1 = 0;
            byte level2 = 0;
            if (m1.Jobs.TryJob(JobTypeIdH.Social, out var job1)) {
                if (job1.Incapable) {
                    return rng.Range(-1f, 0f);
                }
                level1 = job1.Level;
            }
            if (m2.Jobs.TryJob(JobTypeIdH.Social, out var job2)) {
                if (job2.Incapable) {
                    return rng.Range(-1f, 0f);
                }
                level2 = job2.Level;
            }
            return Mathf.Clamp(rng.Range(-1f, 1f) + ((level1 + level2) / 20f), -1f, 1f);
        }

        private float GenerateSexualAttractionFor(BeingData b1, BeingData b2, Rng rng) {
            if (b1 == null || b2 == null) { return 0f; }
            var m1 = b1.Mind;
            if (m1.Species != b2.Species) {
                // TODO inject trait stuff, like robot lover
                return 0f;
            }
            var b2Sex = b2.Body.Sex;
            if (b2Sex == null) {
                return 0f;
            }
            if (Arrays.ContainsSafe(m1.AttractedToSex, b2.Body.Sex)) {
                float bias;
                if (b2.Mind.Traits.HasTrait(TraitIdH.Handsome)
                        || b2.Mind.Traits.HasTrait(TraitIdH.Beautiful)) {
                    bias = rng.Range(0.15f, 0.25f);
                } else if (b2.Mind.Traits.HasTrait(TraitIdH.Ugly)) {
                    bias = rng.Range(-0.25f, -0.15f);
                } else {
                    bias = rng.Range(-0.15f, 0.15f);
                }

                var maxLibido = b1.Body.MaxLibido;
                if (maxLibido == 0f) {
                    b1.Body.MaxLibido = maxLibido = maxLibidoByAgeCurve.Evaluate(
                        b1.Body.AgePercent)
                        + rng.Range(-0.05f, 0.05f);
                }

                // positive gap = they are older
                var ageGap = b2.Body.Age - m1.Age;
                var ageDiff = Mathf.Abs(ageGap) / m1.Species.LifeExpectancyYears;
                float ageImpact;
                if (ageGap > 0) {
                    // young will not be very attracted to older
                    ageImpact = 1f - (ageDiff * 2f);
                } else {
                    // older will still be quite attracted to younger
                    ageImpact = 1f - (ageDiff * 0.5f);
                }

                return Mathf.Clamp(
                    rng.RangeSmooth(0f, maxLibido) * ageImpact + bias,
                        0f, maxLibido);
            }

            return 0f;
        }

        private bool RegisterMind(Mind mind) {
            var id = mind.EnsureId();
            #if DEBUG_MINDS
            D.Warn("Registering mind: {0} -> {1} | GameId: {2}",
                mind.Id, mind, S.GameId);
            #endif
            if (!Minds.TryAdd(id, mind)) {
                D.Err("Mind already added by id: {0}: {1}. Trying to add: {2}",
                    id, Minds[id], mind);
                return false;
            }
            return true;
        }

        private Mind FindMind(uint id) {
            if (id == 0) {
                D.Err("Trying to find mind by id 0!");
                return null;
            }
            if (Minds.TryGetValue(id, out var mind)) {
                return mind;
            }
            D.Err("Can't find mind by id: {0}. Total minds: {1}. Last mind id: {2}. GameId: {3}",
                id, Minds.Count, Mind.LastId, S.GameId);
            return null;
        }

        private List<BeingGroup> GetBeingGroups() {
            if (beingGroups.Count == 0) {
                EnsureDefaultGroups();
            }
            return beingGroups;
        }

        private int LoadBeingsForGroup(BeingGroup group, List<BeingData> result) {
            int found = 0;
            foreach (var b in All.Values) {
                if (b.GroupId != group.IdH) { continue; }
                if (b.Body.HasFlag(BodyFlags.Missing)) { continue; }
                if ((b.StateFlags & group.SkipStates) != BeingState.Uninitialized) {
                    continue;
                }
                if (b.HasStateFlag(BeingState.Dead) && b.HasStateFlag(BeingState.Stored)) {
                    continue;
                }
                if ((group.Flags & BeingGroupFlags.PlayerFaction) == BeingGroupFlags.None) {
                    // never add unavailable beings for non-player faction
                    if (b.Being == null) { continue; }
                }
                result.Add(b);
                found++;
            }
            return found;
        }

        private int CountFriendlyByDef(int speciesidH, bool aliveOnly) {
            int cnt = 0;
            foreach (var b in All.Values) {
                if (b.Species.IdH != speciesidH) { continue; }
                if (aliveOnly && b.HasStateFlag(BeingState.Dead)) {
                    continue;
                }
                // skip biological corpses
                if (b.Species.IsBiological && !b.HasStateFlag(BeingState.Alive)) { continue; }
                // enemy
                if (b.HasStateFlag(BeingState.Alive) && !b.IsPlayer) {
                    continue;
                }
                if (b.HasStateFlag(BeingState.InColony) || b.Body.HasFlag(BodyFlags.Missing)) {
                    continue;
                }
                cnt++;
            }
            return cnt;
        }

        private Dictionary<uint, BeingData> GetAll() {
            return All;
        }

        private BeingStorage storage;
        private void OnBeingChanged(Being being) {
            needsBeingSitRebuild = true;
        }

        private void OnLoadState(GameState state) {
            storage = new BeingStorage(this, state);
            dirtSys = S.Sys.Dirt;
            state.Query.FindBeingData.SetResponder(FindBeingData);
            state.Query.FindStoredBeingData.SetResponder(FindStoredBeingData);
            state.Sig.AddMentalEffect.AddListener(OnAddMentalEffect);
            state.Sig.AddMentalEffectWithLog.AddListener(OnAddMentalEffectWithLog);
            state.Sig.BeingChanged.AddListener(OnBeingChanged);
            RestoreData();
            // Same check ObjsSys runs for item storages: a being whose storage no longer exists
            // is lost with it, and capacity counters are matched to what is really stored
            Ready.When(WhenH.AllSystemsLoaded, () => storage?.ValidateAll(All));
            Ready.Set(WhenH.BeingsLoaded, true);
            state.Clock.OnTick.AddListener(OnTick);
            needsBeingSitRebuild = true;
        }
        private int FindStoredBeingData(int storageId, BeingData[] res, string searchStr,
                BeingData.SortMode sortMode) {
            int found = 0;
            var searchQuery = Strings.ToSearchString(searchStr, true);
            IEnumerable<BeingData> all;
            if (sortMode == BeingData.SortMode.Unsorted) {
                all = All.Values;
            } else {
                all = BeingData.SortedBy(S, All.Values, sortMode);
            }
            foreach (var obj in all) {
                if (obj.StorageId == storageId) {
                    if (!obj.MatchesSearchQuery(searchQuery)) {
                        continue;
                    }
                    res[found++] = obj;
                    if (found == res.Length) {
                        break;
                    }
                }
            }
            return found;
        }

        private void OnAddMentalEffect(BeingData beingData, MentalEffect effect) {
            var mentalHealth = beingData.Mind.Health;
            if (mentalHealth == null) {
                D.Err("Trying to add mental effect for someone who does not have a mental health! {0} / {1}",
                    effect, beingData);
                return;
            }
            mentalHealth.AddEffect(S, effect);
        }
        private void OnAddMentalEffectWithLog(BeingData beingData, MentalEffect effect,
                float autoLogThreshold, bool autoLog) {
            var mentalHealth = beingData.Mind.Health;
            if (mentalHealth == null) {
                D.Err("Trying to add mental effect for someone who does not have a mental health! {0} / {1}",
                    effect, beingData);
                return;
            }
            mentalHealth.AddEffect(S, effect, autoLogThreshold, autoLog);
        }

        private BeingData FindBeingData(uint id) {
            if (id == 0) {
                D.Err("Trying to find being data with id 0!");
                return null;
            }
            All.TryGetValue(id, out var data);
            return data;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Beings ??= new BeingsSysData();
            sd.Beings.Beings = BeingDataData.Serialize(S, All);
            sd.Beings.Minds = MindData.Serialize(Minds);
            sd.Beings.Bonds = BeingBondsData.Serialize(Minds);
            sd.Beings.SurgicalQueue = SurgicalQueueData.Serialize(surgicalQueue);

            // last id can increase during beings serialize, so keep it last
            sd.Beings.LastMindId = Mind.LastId;
            sd.Beings.LastId = BeingData.LastId;
        }

        private BeingsSysData data;

        public void LoadSpecial(SystemsDataSpecial sd) {
            data = sd?.Beings;
        }

        private void RestoreData() {
            if (data == null) {
                RemoveBrokenBeings();
                EnsureDefaultGroups();
                ValidateLastIds();
                return;
            }
            BeingData.LastId = data.LastId;
            if (data.Beings == null) {
                RemoveBrokenBeings();
                EnsureDefaultGroups();
                ValidateLastIds();
                return;
            }
            EnsureDefaultGroups();
            var ticks = S.Clock.Ticks;
            for (int i = 0; i < data.Beings.Length; i++) {
                BeingDataData d = data.Beings[i];
                Being b;
                if (d.BeingId != 0) {
                    b = S.Beings.Find(d.BeingId);
                } else {
                    b = null;
                }
                if (BeingDataData.TryDeserialize(S, d, b, out var bd)) {
                    if (bd.Being == null && ((bd.StateFlags & ~BeingState.Alive) == BeingState.Uninitialized)) {
                        D.Err("Deserialized Active-only being with no Being! Skipping: {0}", bd);
                        continue;
                    }
                    All[bd.Id] = bd;
                }
            }
            RemoveBrokenBeings();
            MindData.Deserialize(data, Minds);
            Mind.LastId = data.LastMindId;

            // Store surgical queue data before clearing data
            var surgicalQueueData = data.SurgicalQueue;
            data = null;

            LinkBeingMinds();
            UpdateImportantBonds();
            AutoAssignBeingGroups();
            foreach (var bd in All.Values) {
                ValidateAfterDeserialize(bd);
            }
            var showNames = The.Prefs.GetBool(
                Pref.GShowOnlySelectedNames, PrefH.GShowOnlySelectedNames, false);
            foreach (var b in S.Beings.All) {
                b.Name.ShowOnSelectChanged(showNames);
            }
            ValidateLastIds();

            // AI goal will be ready only then
            Ready.When(WhenH.AIAgentsLoaded, () => {
                SurgicalQueueData.Deserialize(surgicalQueueData, surgicalQueue, All);
            });
            Ready.When(WhenH.ObjsLoaded, LinkObjMinds);
        }

        /// <summary>
        /// True if a being is flagged InDevice but there's actually no device underneath
        /// it - i.e. it was orphaned by a device that stopped managing it (e.g. torn down,
        /// or the patient reference desynced) without going through the normal eject path.
        /// </summary>
        private static bool IsOrphanedInDevice(BeingData bd) {
            if (!bd.HasStateFlag(BeingState.InDevice) || bd.Being == null) {
                return false;
            }
            if (EntityUtils.ObjAt(Pos.FromVector(bd.Being.Graphics.Position)) != null) {
                return false;
            }
            return bd.Being.Goal == null
                && bd.Being.AIAgent.Goal == null
                && !bd.Being.Graphics.IsHidden;
        }

        /// <summary>
        /// Strips every piece of state a device may leave on a patient (InDevice flag,
        /// Operation immobilization, Offline BatteryConsumption modifier) and restores
        /// looks/posture - mirroring BodyModSpotComp.EjectPatient's cleanup, for a being
        /// the device itself has already lost track of.
        /// </summary>
        private static void ClearOrphanedDeviceState(BeingData bd) {
            var being = bd.Being;
            D.Err("Cleaning orphaned device state for being: {0}", bd);
            bd.StateFlags &= ~BeingState.InDevice;
            being.Motion.Immobilize(ImmobilizeReasonIdH.Operation, false);
            BodyModSpotComp.RemoveOfflineConsumption(bd);
            bd.Body.CheckCondition(false);
            if (being.IsActive) {
                if (!being.Motion.IsImmobilized && !bd.Body.IsIncapacitated) {
                    being.Graphics.SetVertical(true);
                    being.Looks.TogglePosFilters(true, false);
                }
                BeingUtils.UpdateBodyInfoIcon(being);
                being.Graphics.RebuildLooks(force: true);
            }
            being.Name.CheckState();
        }

        /// <summary>
        /// Runtime counterpart to the load-time orphan check below: a device can desync
        /// from its patient mid-session too (e.g. its Patient reference is lost without
        /// running EjectPatient), so this re-checks live beings on a slow cadence from
        /// OnTick. Requires the orphan condition to hold across two consecutive checks
        /// before healing, to rule out a being just passing through its device tile.
        /// </summary>
        private void CheckDeviceOrphan(BeingData bd, long ticks) {
            if (!IsOrphanedInDevice(bd)) {
                suspectedDeviceOrphans.Remove(bd);
                return;
            }
            if (suspectedDeviceOrphans.TryGetValue(bd, out var suspectedSince)) {
                if (ticks - suspectedSince >= Consts.TicksPer15Minutes) {
                    D.Err("Being has InDevice state, but no device underneath! Removing state: {0} | {1}",
                        bd, bd.Being);
                    ClearOrphanedDeviceState(bd);
                    suspectedDeviceOrphans.Remove(bd);
                }
            } else {
                suspectedDeviceOrphans[bd] = ticks;
            }
        }

        // A raider that had already left the map (Hidden, no entity) could be turned by the
        // Injured Raider event and showed up as crew who exists nowhere (STA-1882). Every
        // legitimate entity-less crew state carries Missing (deserters, stasis, manifest
        // sleepers, storage) or Expedition / Stored / InDevice / InColony, so only this exact
        // shape is a phantom.
        private static bool IsPhantomCrew(BeingData bd) {
            return bd.Being == null
                && bd.StateFlags == (BeingState.Alive | BeingState.Hidden)
                && !bd.Body.HasFlag(BodyFlags.Missing)
                && bd.Mind?.Faction?.IsPlayer == true
                && !bd.IsInStasis
                && bd.StorageId == 0;
        }

        private void RemovePhantomCrew(BeingData bd) {
            D.Warn("Removing crew member that is nowhere on the map: {0}", bd);
            bd.Body.Flags |= BodyFlags.Missing;
            Ready.When(WhenH.AllSystemsLoaded, () => {
                S.Query.AssignBedSlot.Ask(bd, null);
                S.Query.AssignBotDock.Ask(bd, null);
            });
        }

        private void ValidateAfterDeserialize(BeingData bd) {
            if (bd.HasStateFlag(BeingState.InDevice) && bd.Being != null) {
                if (EntityUtils.ObjAt(Pos.FromVector(bd.Being.Graphics.Position)) == null) {
                    Ready.When(WhenH.AllSystemsLoaded, () => {
                        if (IsOrphanedInDevice(bd)) {
                            D.Err("Being has InDevice state, but no device underneath! Removing state: {0} | {1}",
                                bd, bd.Being);
                            ClearOrphanedDeviceState(bd);
                        }
                    });
                }
            }
            // Dead with no entity and no flag saying where it went: an abandoned section used
            // to leave these behind (CmdAbandonSection called Beings.Destroy instead of
            // Eliminate). Every legitimate entity-less dead state is also Hidden, Stored,
            // Expedition, InDevice or InColony.
            if (bd.Being == null && bd.StateFlags == BeingState.Dead) {
                D.Warn("Hiding leaked dead being without an entity: {0}", bd);
                bd.StateFlags |= BeingState.Hidden;
                bd.Body.Flags |= BodyFlags.Missing;
                bd.DeactivateConditionsOnDeath();
            }
            if (IsPhantomCrew(bd)) {
                RemovePhantomCrew(bd);
            }
            if (bd.Mind == null) {
                D.Err("Being with null mind: {0}", bd);
                return;
            }
            GenBeingBodyDataStep.ValidateBodyColors(bd);
            if (bd.Species.IsBiological && !Arrays.IsEmpty(bd.Mind.AttractedToSex)) {
                if (bd.Body.MaxLibido == 0f) {
                    bd.Body.MaxLibido = GenBeingIntialVarsDataStep.GenerateLibido(
                        Rng.Unseeded, bd.Body.Age, bd.Species);
                    // D.Warn("Generated libido for: {0} > {1}", bd.Body.MaxLibido, bd);
                }
            }
        }

        private void UpdateImportantBonds() {
            foreach (var being in All.Values) {
                being.Mind.Bonds?.RebuildImportant();
            }
        }

        private void LinkBeingMinds() {
            // D.Warn("Linking being minds");
            foreach (var being in All.Values) {
                Mind mind;
                if (being.Mind != null) {
                    D.Warn("Legacy being regenerated, saving mind: {0}", being);
                    RegisterMind(being.Mind);
                    mind = being.Mind;
                } else {
                    if (Minds.TryGetValue(being.MindId, out mind)) {
                        mind.SetOwner(being);
                    } else {
                        D.Err("PANIC: No mind to link to being: {0} (mind id: {1}). Regenerating",
                            being, being.MindId);
                        if (being.HasStateFlag(BeingState.Alive)) {
                            var vars = new GenBeingVars(being.Species, BeingType.Neutral);
                            mind = S.Query.GenBabyMind.Ask(vars, Rng.Unseeded);
                            mind.Age = being.Body.Age;
                        } else {
                            mind = Mind.Blank(BeingName.BlankFor(being.Species), being.Species);
                        }
                        mind.SetOwner(being);
                    }
                }
                being.MindId = 0;
                if (!mind.IsBlank && being.HasStateFlag(BeingState.Alive)) {
                    mind.Sex ??= being.Body.Sex;
                }
                being.Being?.Transmit(null, MsgIdH.BeingFinishedLoading);
                mind.AfterSetFaction(true);
                EnsureBlankFactionForMindless(being);
                if (!being.SkipSimulate) {
                    // D.Warn("Adding active being: {0}", bd);
                    ActiveBeings.Add(being);
                    being.ApplyForActiveBeing(S, true);
                }
                if (being.Being != null) {
                    try {
                        being.ApplyForSpawnedBeing(S, true);
                    } catch (Exception ex) {
                        D.LogEx(ex, "Failed loading a being: {0}", being);
                    }
                } else {
                    being.Mind.Name.UpdateName();
                }
            }
        }

        private void ValidateLastIds() {
            if (All.Count > 0) {
                var savedLastIdBD = BeingData.LastId;
                var actualLastIdBD = All.Keys.Max();
                if (actualLastIdBD > savedLastIdBD) {
                    D.Err("BeingData.LastId is saved incorrectly: {0}. Actual: {1}",
                        savedLastIdBD, actualLastIdBD);
                    BeingData.LastId = actualLastIdBD;
                }
            }
            if (Minds.Count > 0) {
                var savedLastIdMind = Mind.LastId;
                var actualLastIdMind = Minds.Keys.Max();
                if (savedLastIdMind > actualLastIdMind) {
                    D.Err("Mind.LastId is saved incorrectly: {0}. Actual: {1}",
                        savedLastIdMind, actualLastIdMind);
                    Mind.LastId = actualLastIdMind;
                }
            }
        }

        private void LinkObjMinds() {
            var objs = S.Query.GetAllObjs.Ask();
            foreach (var obj in objs.Values) {
                if (obj.MindId == 0) { continue; }
                if (Minds.TryGetValue(obj.MindId, out var mind)) {
                    Mind.LinkToObj(mind, obj);
                    // Multiple objects can link to same mind! Example:
                    // ashes and brains obj
                    // mind.ObjId = obj.Id; <- don't do this!
                } else if (obj.MindId != 0) {
                    D.Err("Failed to load linked object mind by id: {0} -> {1}",
                        obj.MindId, obj);
                }
                obj.MindId = 0;
            }
            foreach (var mind in Minds.Values) {
                if (mind.ObjId == 0) { continue; }
                if (objs.TryGetValue(mind.ObjId, out var obj)) {
                    if (obj.Mind != mind) {
                        D.Err("Mind mismatch on obj! {0} expected mind {1}, actual mind {2}",
                            obj, mind, obj.Mind);
                    }
                } else {
                    // object not found, could be destroyed
                    #if !PRODUCTION_BUILD
                    D.Err("Obj not found for mind: {0}", mind, mind.ObjId);
                    #else
                    // D.Warn("Obj not found for mind: {0}", mind, mind.ObjId);
                    #endif
                    mind.ObjId = 0;
                }
            }
        }

        private static void EnsureBlankFactionForMindless(BeingData bd) {
            // fix faction
            if (bd.Species.IsBiological) {
                if (bd.Body.TryGetSlot(BodyPartSlotIdH.Brain, out var brainSlot)) {
                    if (brainSlot.IsPartRemoved && !bd.Mind.Faction.IsBlank) {
                        D.ErrSoft("Fixing wrong faction for brainless human: {0} | {1}",
                            bd.Mind.Faction, bd);
                        bd.Mind.SetFaction(FactionDef.Blank, false);
                        bd.GroupId = 0;
                    }
                }
            } else {
                if (bd.Body.TryGetSlot(BodyPartSlotIdH.Core, out var coreSlot)) {
                    if (coreSlot.IsPartRemoved && !bd.Mind.Faction.IsBlank) {
                        D.ErrSoft("Fixing wrong faction for coreless bot: {0} | {1}",
                            bd.Mind.Faction, bd);
                        bd.Mind.SetFaction(FactionDef.Blank, false);
                        bd.GroupId = 0;
                    }
                }
            }
        }

        private void AutoAssignBeingGroups() {
            foreach (var b in All.Values) {
                if (b.GroupId != 0) { continue; }
                BeingGroup.AssignFor(b);
            }
        }

        private void EnsureDefaultGroups() {
            BeingGroup.EnsureDefaults(S, beingGroups);
        }

        private void RemoveBrokenBeings() {
            List<(Species, int)> newBeingsToCreate = null;
            int bioSpawnSpot = 0;
            int roboSpawnSpot = 0;
            for (int i = S.Beings.All.Count - 1; i >= 0; i--) {
                Being b = S.Beings.All[i];
                if (b.Data != null) {
                    if ((b.Data.Body.Flags & BodyFlags.Missing) == BodyFlags.Missing) {
                        D.Err("Removing missing flag from the body of being that is created: {0}", b);
                        b.Data.Body.Flags &= ~BodyFlags.Missing;
                    }
                    continue;
                }
                newBeingsToCreate ??= new();
                var pos = b.PosIdx;
                var species = Species.ForDef(b.Definition.IdH);
                if (pos == 0) {
                    if (bioSpawnSpot == 0) {
                        var bed = S.Components.FindFirstByType<BedComp>();
                        if (bed != null) {
                            bioSpawnSpot = bed.Tile.PosIdx;
                        } else {
                            var fallback = S.Components.FindFirstByType<ElectricNodeComp>();
                            if (fallback != null) {
                                bioSpawnSpot = fallback.Tile.PosIdx;
                            } else {
                                bioSpawnSpot = S.Grid.CenterPos;
                            }
                        }
                    }
                    if (roboSpawnSpot == 0) {
                        var cha = S.Components.FindFirstByType<ChargeStationComp>();
                        if (cha != null) {
                            roboSpawnSpot = cha.Tile.PosIdx;
                        } else {
                            roboSpawnSpot = bioSpawnSpot;
                        }
                    }
                    // let's find a position
                    // D.Err("Removing a being without data with pos 0! {0}",
                    //     D.DumpInstance(b));
                    if (species.IsBiological) {
                        pos = bioSpawnSpot;
                    } else {
                        pos = roboSpawnSpot;
                    }
                }
                newBeingsToCreate.Add((species, pos));
                D.Err("Removing being that does not have BeingData after load: {0}", D.DumpInstance(b));
                S.Beings.Destroy(b, true);
            }
            if (newBeingsToCreate != null) {
                Ready.When(WhenH.AllSystemsLoaded, () => {
                    foreach (var nb in newBeingsToCreate) {
                        var data = BeingUtils.GenBeingWithInventory(
                            new GenBeingVars(nb.Item1, BeingType.Friendly),
                            Rng.Unseeded, GenItemFlags.Casual | GenItemFlags.SpaceReady | GenItemFlags.Weaponized);
                        var b = S.Beings.Create(nb.Item2, data);
                        D.Err("Recreated legacy being {0}", b);
                    }
                });
            }
        }

        /// <summary>
        /// Creates (generates) a full being data without creating an instance
        /// </summary>
        /// <returns>new BeingData with everything, procgen</returns>
        private BeingData GenerateBeing(GenBeingVars vars, Rng rng) {
            if (vars == null) {
                D.Err("Trying to generate being data with null vars! {0}", vars);
                return null;
            }
            var d = new BeingData();
            // d.Faction = FactionDef.Player;
            d.Species = vars.Species;
            d.StatsBeing = new StatsBeing();
            d.StatsBeing.ActivateFor(d);
            d.StatsBeing.FinalizeStats();
            d.Counters = new Counters();
            d.Counters.Initialize(d);
            GenerateMissingParts(d, rng, vars);
            return d;
        }

        private Mind GenBabyMind(GenBeingVars vars, Rng rng) {
            var name = BeingName.BlankFor(vars.Species);
            var mind = Mind.Blank(name, vars.Species);
            // TODO use vars.sex if set
            var sex = vars.Species.RandomSex(rng);
            GenBeingMindDataStep.GenerateName(S, vars.Species, vars.BeingType,
                Rng.Unseeded, mind, sex?.TypeChar ?? 'N', vars.ExceptNames);
            // D.Warn("generated name for baby mind: {0} ({1})", mind.Name, species);
            mind.Age = 0;
            mind.BirthTick = G.Ticks;
            mind.Experience = new Experience();
            mind.Experience.Roles = new();
            if (sex != null) {
                var sexOr = vars.Species.RandomSexualOrientation(rng);
                mind.AttractedToSex = sexOr.Attractions(vars.Species, sex);
            }
            GenBeingJobsDataStep.GenerateCoreJobs(mind, null);
            mind.Traits = new BeingTraits();
            GenBeingMindDataStep.GenerateMentalHealth(vars.Species, rng, mind);
            if (mind.MentalStats == null) {
                mind.MentalStats = new();
            }
            // new mind will obey
            mind.MentalStats[StatIdH.AIAlignment] = 1f;
            // we will update this later
            // data.StatsBeing.AIAlignment.UpdateBase(1f);
            mind.SetFaction(FactionDef.Player);
            return mind;
        }

        private bool GenerateMissingParts(BeingData data, Rng rng) {
            return GenerateMissingParts(data, rng, null);
        }
        private bool GenerateMissingParts(BeingData data, Rng rng, GenBeingVars vars) {
            return GenBeingData.Generate(S, rng, data, vars);
        }

        /// <summary>
        /// Only called once the being gets spawned, not on save / load
        /// </summary>
        private bool Activate(BeingData data) {
            // D.Warn("Activating being: {0}", data);
            if (data.Being == null) {
                D.Err("Trying to activate a data while data.Being == null: {0}",
                    D.DumpInstance(data));
                return false;
            }
            var isDead = data.HasStateFlag(BeingState.Dead);
            // this is VERY BAD! All.Add doesn't happen
            // if (isDead) {
            //     // data.AfterLoad(S, false);
            //     // BeingUtils.UpdateFlagsFor(S, data);
            //     // return false;
            // }
            if (data.Id == 0) {
                data.Id = ++BeingData.LastId;
                if (!All.TryAdd(data.Id, data)) {
                    D.Err("Trying to add being with duplicate id: {0}. Being: {1}. Existing: {2}",
                        data.Id, data, All[data.Id]);
                    return false;
                }
                RegisterMind(data.Mind);
            }

            bool wasInitialized = data.StateFlags != BeingState.Uninitialized;
            if (!wasInitialized) {
                data.StateFlags |= BeingState.Alive;
            }
            if (!isDead) {
                ActiveBeings.Add(data);
                data.ApplyForActiveBeing(S, false);
            }
            data.ApplyForSpawnedBeing(S, false);
            if (!wasInitialized) {
                InitializeBeing(data);
            }

            BeingUtils.UpdateFlagsFor(S, data);
            data.Inventory?.Update();
            S.Sig.BeingAdded.Send(data.Being);
            data.Being.Transmit(null, MsgIdH.BeingActivated);
            BeingGroup.AssignFor(data);
            S.Sig.RebuildBeingGroups.Send();

            return true;
        }

        // The identity half of Activate without the map half: a stasis manifest sleeper has
        // to exist (id, saved, mind known to relationships) long before it has a Being.
        // First-time init is deliberately left for the wake-up, see
        // StasisUtils.WakeManifestSleeper.
        private bool RegisterDormant(BeingData data) {
            if (data.Id != 0) {
                D.Err("Trying to register an already registered being as dormant: {0}", data);
                return false;
            }
            data.Id = ++BeingData.LastId;
            if (!All.TryAdd(data.Id, data)) {
                D.Err("Trying to add dormant being with duplicate id: {0}. Being: {1}. Existing: {2}",
                    data.Id, data, All[data.Id]);
                return false;
            }
            RegisterMind(data.Mind);
            data.StateFlags = BeingState.Alive | BeingState.Hidden;
            data.Body.Flags |= BodyFlags.Missing;
            // Reserved now, so a colonist generated later cannot share a name with someone
            // the player has already read on the manifest
            data.Mind.Name.Consume(data);
            return true;
        }

        private void InitializeBeing(BeingData data) {
            data.Mind.Name.Consume(data);
            data.Inventory?.Initialize(S);
            foreach (var n in data.NeedSources) {
                n.Need.InitializeForNew(data);
            }
            BeingGroup.AssignFor(data);
            S.Sig.BeingInitialized.Send(data);
            if (data.GroupId != 0) {
                S.Sig.RebuildBeingGroups.Send();
            }
        }

        public bool Deactivate(BeingData data) {
            if (data.HasStateFlag(BeingState.Alive) && !data.HasStateFlag(BeingState.Hidden)) {
                D.Err("Trying to deactivate a being with target state that is alive and not hidden: {0}",
                    data.StateFlags);
                return false;
            }
            ActiveBeings.Remove(data);
            data.AfterEntityRemoved(S);
            if (data.Being?.IsActive == true) {
                D.Err("PANIC: Trying to deactivate BeingData when data.Being is active! {0} | {1}", data, data.Being);
            }
            data.Being = null;
            if (data.StateFlags == BeingState.Uninitialized) {
                D.Warn("Completely removing BeingData: {0}", data);
                All.Remove(data.Id);
                S.Sig.BeingUninitialized.Send(data);
            }
            BeingGroup.AssignFor(data);
            S.Sig.RebuildBeingGroups.Send();
            return true;
        }

        private void RebuildBeingsSituation() {
            needsBeingSitRebuild = false;
            var sit = S.Situation;
            var hadBeingsToProtect = SafetyCheckComp.HasBeingsToProtect(S);
            sit.Enemies = 0;
            sit.Friendlies = 0;
            sit.Workers = 0;
            sit.Colonists = 0;
            sit.Guests = 0;
            sit.Firefighters = 0;
            float weightedColonySize = 0f;
            foreach (var b in All.Values) {
                if (b.HasStateFlag(BeingState.Dead)) { continue; }
                if (b.Body.HasFlag(BodyFlags.Missing)) { continue; }
                // Ahead of IsPlayer on purpose: EffectiveFaction collapses to Player for
                // obedient / high alignment beings, so testing IsPlayer first would let some
                // guests count as crew depending on their alignment stat.
                if (b.IsGuest) {
                    sit.Guests++;
                    continue;
                }
                if (b.IsPlayer) {
                    sit.Friendlies++;
                    if (b.Species.IsPet) {
                        // pets count as half a colonist in colony size diff
                        weightedColonySize += 0.5f;
                    } else {
                        weightedColonySize += 1f;
                    }
                    var species = b.Species;
                    if (species.IsBiological) {
                        if (species.IsHumanoid) {
                            sit.Colonists++;
                        }
                    } else {
                        sit.Workers++;
                    }
                    if (b.Mind.Jobs.CanDo(JobTypeIdH.Firefighting)) {
                        sit.Firefighters++;
                    }
                } else {
                    sit.Enemies++;
                }
            }
            sit.Population = weightedColonySize;
            sit.ColonySizeDiff = 16 - Mathf.FloorToInt(weightedColonySize);
            // Devices stay quiet about unsafe surroundings while nobody aboard could be hurt
            // by them. Re-check them all the moment that changes (normally the first stasis
            // wake-up) - waiting for the next rare tick would leave the player without
            // warnings they can finally act on for up to 15 in-game minutes.
            if (!hadBeingsToProtect && SafetyCheckComp.HasBeingsToProtect(S)) {
                SafetyCheckComp.RecheckAll(S);
            }
        }
    }
}
