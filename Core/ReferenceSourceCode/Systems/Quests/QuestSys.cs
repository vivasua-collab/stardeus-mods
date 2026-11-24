#if UNITY_EDITOR
// #define DEBUG_QUESTS
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using Game.Data.Space;
using KL.Grid;
using Game.Components;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Ships;
using Game.Systems.Story;
using KL.Randomness;
using Unity.Mathematics;
using Game.Audio;
using Game.Data.Economy;
using UnityEditor;
using Game.Systems.Space;
using UnityEngine.Pool;

namespace Game.Systems.Quests {
    public sealed class QuestSys : GameSystem, ISaveableSpecial {
        public const string SysId = "QuestSys";
        public override string Id => SysId;
        // public override bool SkipInSandbox => true;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new QuestSys());
        }

        public readonly List<Quest> ActiveQuests = new();
        public readonly List<Quest> AllPlayerQuests = new();
        public QuestNotifications Notifications;
        private QuestActivator activator;
        public readonly Dictionary<QuestStep, QuestStepTarget> StepTargets = new();
        public readonly Dictionary<uint, QuestPackage> QuestPackages = new();
        private readonly Dictionary<IQuestProvider, Quest> ProviderQuests = new();
        public readonly Dictionary<QuestDef, QuestSeenData> Seen = new();
        private readonly Dictionary<SpaceRegion, List<Quest>> hubQuests = new();
        private readonly Dictionary<SpaceRegion, long> nextHubRefreshTicks = new();
        private readonly List<IInteractiveQuestStep> globalInteractiveSteps = new();
        public Quest TrackedQuest;
        public QuestBools Bools;
        private QuestSysData data;
        /// <summary>
        /// True while RestoreData is rebuilding quests from a saved game.
        /// Guards TryFireOnStartTutorial so loading never re-fires tutorial popups.
        /// </summary>
        private bool isRestoringQuests;
        public bool IsRestoringQuests => isRestoringQuests;
        private QuestGen gen;
        private long NextNPCShipQuestOfferAt;
        private Curve npcShipQuestDelayDaysCurve;
        private Curve npcShipQuestDelayDaysGlobalCurve;

        /// <summary>
        /// Used to share global persistent data among different quests.
        /// Mainly for main story line.
        /// </summary>
        private ComponentData sharedData;

        protected override void OnInitialize() {
            Notifications = new QuestNotifications(S, this);
            gen = new QuestGen(S, this);
            activator = new QuestActivator(S, this);

            S.Sig.AfterLoadState.AddListener(OnLoadSave);

            // registered early because it's required for generating NPC ships
            S.Query.IsActiveQuestProvider.SetResponder(IsActiveQuestProvider);
            S.Query.CanOfferColonistQuest.SetResponder(CanOfferColonistQuest);
            S.Query.IsActiveQuestTargetShip.SetResponder(IsActiveQuestTargetShip);
            S.Query.IsMindRequiredByQuest.SetResponder(IsMindRequiredByQuest);

            npcShipQuestDelayDaysCurve = CurveDef.FastGet(CurveIdH.Quests_QuestDelayDays_NPCShip);
            npcShipQuestDelayDaysGlobalCurve = CurveDef.FastGet(CurveIdH.Quests_QuestDelayDays_NPCShipGlobal);
        }

        private void OnLoadSave(GameState state) {
            S.Clock.OnTick.AddListener(OnTick);
            // Also runs on a paused clock, which never ticks
            S.Clock.EveryTickOrFrame.AddListener(activator.Update);
            S.Sig.QuestStateChanged.AddListener(OnQuestStateChange);
            S.Sig.QuestStepStateChanged.AddListener(OnQuestStepStateChange);
            S.Sig.SkipTutorial.AddListener(OnSkipTutorial);
            S.Query.AddQuest.SetResponder(AddQuest);
            S.Query.AcceptQuest.SetResponder(AcceptQuest);
            S.Query.RegisterQuestStepTarget.SetResponder(RegisterStepTarget);
            S.Query.RegisterAvailableQuest.SetResponder(RegisterAvailableQuest);
            S.Query.GetQuestStepTargets.SetResponder(GetStepTargets);
            S.Query.TrackQuest.SetResponder(TrackQuest);
            S.Query.IsTrackedQuest.SetResponder((q) => q == TrackedQuest);
            S.Query.AddQuestPackage.SetResponder(AddPackage);
            S.Query.DestroyQuestPackage.SetResponder(DestroyPackage);
            S.Query.SpawnQuestPackage.SetResponder(SpawnPackage);
            S.Query.OpenQuestPackage.SetResponder(OpenPackage);
            S.Query.FindQuestPackage.SetResponder(FindPackage);
            S.Query.GetQuestPackages.SetResponder(GetQuestPackages);
            S.Query.GetActiveQuests.SetResponder(() => ActiveQuests);
            S.Query.GetAllPlayerQuests.SetResponder(() => AllPlayerQuests);
            S.Query.GetTrackedQuest.SetResponder(() => TrackedQuest);
            S.Query.GetQuestFor.SetResponder(GetQuestFor);
            S.Query.ForceQuestFor.SetResponder(ForceQuestFor);
            S.Query.DelayNextNPCShipQuest.SetResponder(DelayNextNPCShipQuest);
            S.Query.WantsToOfferQuestToPlayer.SetResponder(WantsToOfferQuest);
            S.Query.UpdateQuestSeen.SetResponder(UpdateSeen);
            S.Query.GetQuestVariantIndex.SetResponder(PickQuestVariantIndex);
            S.Query.MarkQuestVariantSeen.SetResponder(MarkQuestVariantSeen);
            S.Query.SetQuestBool.SetResponder(SetBool);
            S.Query.GetQuestBool.SetResponder(GetBool);
            S.Query.GetSharedQuestData.SetResponder(GetSharedQuestData);
            S.Sig.SpaceObjectRemoved.AddListener(OnSORemoved);
            S.Sig.UniverseRegenerated.AddListener(OnUniverseRegen);
            S.Sig.BeingDied.AddListener(OnBeingDied);
            S.Sig.ShipTurnedDerelict.AddListener(OnShipTurnedDerelict);
            S.Query.GetGlobalInteractiveQuestSteps.SetResponder(() => globalInteractiveSteps);
            S.Query.RegisterGlobalInteractiveQuestStep.SetResponder(RegisterGlobalInteractiveStep);
            S.Query.ResetAllQuests.SetResponder(ResetAllQuests);
            S.UIQuery.ShowQuestHubFor.SetResponder(ShowQuestHubFor);
            Bools ??= QuestBools.Create();
            if (data != null && data.AllPlayerQuests != null) {
                // Ready.When(WhenH.AllSystemsLoaded, RestoreData);
                S.Clock.ExecuteAfterFramesForUI(1, RestoreData);
            } else {
                // Seen is saved apart from the quests, and a save without any persisted
                // quest still has it (declined offers, seen variants). Skipped here, that
                // memory was lost and the next save overwrote it. Before LoadQuests, which
                // reads it for the auto-add rotation.
                if (data != null) {
                    QuestSeenData.Deserialize(data.Seen, Seen);
                }
                activator.LoadQuests();
                // A loaded save can have zero ever-persisted quests (AllPlayerQuests
                // serializes as null) while still holding a signal with a dangling
                // QuestId from an unaccepted offer - RestoreData won't run to sweep it.
                S.Clock.ExecuteAfterFramesForUI(1, RemoveOrphanedSignalOffers);
            }
        }

        private bool ShowQuestHubFor(SpaceObject obj) {
            var region = SOUtils.RegionFor(obj);
            var quests = UpdateQuestHubForRegion(region);
            S.UIQuery.ShowQuestHubQuests.Ask(quests);
            return true;
        }

        private List<Quest> UpdateQuestHubForRegion(SpaceRegion region) {
            if (!hubQuests.TryGetValue(region, out var quests)) {
                quests = new List<Quest>();
                hubQuests[region] = quests;
            }
            var ticks = G.Ticks;
            for (int i = quests.Count - 1; i >= 0; i--) {
                Quest q = quests[i];
                if (q.Data.GetNextDeadline(out var deadline, out _)) {
                    if (deadline < ticks) {
                        quests.RemoveAt(i);
                        // Never accepted, so nothing else ends it: its ship stayed an active
                        // provider and never offered a quest again this session
                        var prov = q.Data.Provider;
                        if (q.Data.State == QuestState.Undefined && prov != null
                                && ProviderQuests.TryGetValue(prov, out var provQuest)
                                && provQuest == q) {
                            ProviderQuests.Remove(prov);
                        }
                    }
                }
            }
            nextHubRefreshTicks.TryGetValue(region, out var refreshTick);
            if (ticks > refreshTick) {
                nextHubRefreshTicks[region] = ticks +
                    Rng.URange(Consts.TicksPer2Weeks, Consts.TicksPer3Weeks);
                var wantedQuests = Rng.URange(1, 4);
                int attempts = 10;
                var regionShips = ListPool<NPCShip>.Get();
                foreach (var sec in region.Sectors) {
                    var secShips = S.Query.NPCShipsAt.Ask(sec);
                    if (!Arrays.IsEmpty(secShips)) {
                        foreach (var ship in secShips) {
                            if (ship.IsPlayer) { continue; }
                            if (ship.NoQuests) { continue; }
                            if (ship.NextQuestAvailableAt < ticks) {
                                regionShips.Add(ship);
                            }
                        }
                    }
                }
                while (quests.Count < wantedQuests && attempts-- > 0) {
                    // each attempt takes a ship off the list; drawing from an empty one gave
                    // a null provider, and GetQuestFor threw on it
                    if (regionShips.Count == 0) { break; }
                    var q = GenerateHubQuestFor(region, regionShips);
                    if (q != null) {
                        quests.Add(q);
                    }
                }
                regionShips.Clear();
                ListPool<NPCShip>.Release(regionShips);
            }

            return quests;
        }

        private Quest GenerateHubQuestFor(SpaceRegion region, List<NPCShip> regionShips) {
            var ship = Rng.UFrom(regionShips);
            regionShips.Remove(ship);
            return GetQuestFor(ship, true, true);
        }

        private bool ResetAllQuests() {
            Seen.Clear();
            AllPlayerQuests.Clear();
            ActiveQuests.Clear();
            TrackedQuest = null;
            activator.LoadQuests();
            return true;
        }

        private bool RegisterGlobalInteractiveStep(IInteractiveQuestStep step, bool add) {
            if (add) {
                return globalInteractiveSteps.AddUniqueInefficient(step);
            } else {
                return globalInteractiveSteps.Remove(step);
            }
        }

        private void OnSORemoved(SpaceObject so) {
            if (so.IsShip) { return; }
            // don't care about shuttles, copters
            if (so.Type.CanMoveInStarmap) { return; }
            // check inactive quests
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = AllPlayerQuests[i];
                if (q.Data.Provider == so && (q.IsActive || q.IsAvailable)) {
                    q.Data.SetState(QuestState.Failed);
                    q.Data.Provider = null;
                }
            }
            FailStepTargetsReferencing(so);
        }

        // A quest step can reference an SO as its interaction target (Var_Target) without it
        // being the quest's Provider (e.g. InteractWithSOQuestStep hailing a SectorPOI). If that
        // SO is removed while the step is active and registered, the step can never complete -
        // fail it (and, via FailsQuest, the quest) instead of leaving it soft-locked forever.
        private void FailStepTargetsReferencing(SpaceObject so) {
            List<QuestStep> toFail = null;
            foreach (var st in StepTargets.Values) {
                if (st.Target != so) { continue; }
                // A step can remove its own target SO as part of successfully completing
                // (e.g. ExplorePOIQuestStep/DeliverPassengerToPOIQuestStep -> ExpeditionStageQuest.
                // RemovePOI), before its deferred Cleanup() has unregistered the target - only a
                // step still actively waiting on the SO should be failed here, not one that just
                // completed as a result of the very removal that triggered this.
                if (!st.Step.IsActive) { continue; }
                (toFail ??= new List<QuestStep>()).Add(st.Step);
            }
            if (toFail == null) { return; }
            foreach (var step in toFail) {
                step.SetState(QuestStepState.Failed, warnIfSame: false);
            }
        }

        // The new universe reuses SpaceObject ids, so a quest kept across a regeneration
        // would resolve its saved targets to unrelated objects after a load.
        private void OnUniverseRegen() {
            hubQuests.Clear();
            nextHubRefreshTicks.Clear();
            List<Quest> toFail = null;
            foreach (var q in AllPlayerQuests) {
                if ((q.IsActive || q.IsAvailable) && ReferencesOldUniverse(q.Data)) {
                    (toFail ??= new List<Quest>()).Add(q);
                }
            }
            if (toFail != null) {
                foreach (var q in toFail) {
                    if (q.IsEnded) { continue; }
                    q.Data.SetState(QuestState.Failed);
                    q.Data.Provider = null;
                }
            }
            List<IQuestProvider> staleProviders = null;
            foreach (var prov in ProviderQuests.Keys) {
                if (IsOldUniverse(prov)) {
                    (staleProviders ??= new List<IQuestProvider>()).Add(prov);
                }
            }
            if (staleProviders != null) {
                foreach (var prov in staleProviders) {
                    ProviderQuests.Remove(prov);
                }
            }
        }

        private bool ReferencesOldUniverse(QuestData data) {
            if (IsOldUniverse(data.Provider)) { return true; }
            if (data.Variables != null) {
                foreach (var v in data.Variables.Values) {
                    if (v is IQuestVar<SpaceObject> soVar && IsOldUniverse(soVar.Value)) {
                        return true;
                    }
                    if (v is IQuestVar<NPCShip> shipVar && IsOldUniverse(shipVar.Value)) {
                        return true;
                    }
                }
            }
            foreach (var st in StepTargets.Values) {
                if (st.Step.Data == data && IsOldUniverse(st.Target)) { return true; }
            }
            return false;
        }

        private bool IsOldUniverse(IQuestProvider prov) {
            var so = prov switch {
                NPCShip ship => ship.SO,
                SpaceObject spaceObj => spaceObj,
                _ => null,
            };
            if (so == null) { return false; }
            return !S.Universe.TryFind(so.Id, out var current) || current != so;
        }

        private void OnBeingDied(BeingData died) {
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = AllPlayerQuests[i];
                if (q.Data.Provider is ColonistQuestProvider cp
                        && cp.BeingData.Id == died.Id
                        && (q.IsActive || q.IsAvailable)) {
                    q.Data.SetState(QuestState.Failed);
                    q.Data.Provider = null;
                }
            }
        }

        private void OnShipTurnedDerelict(NPCShip ship) {
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = AllPlayerQuests[i];
                if (q.Data.Provider == ship && (q.IsActive || q.IsAvailable)) {
                    q.Data.SetState(QuestState.Failed);
                    q.Data.Provider = null;
                }
            }
        }

        private ComponentData GetSharedQuestData() {
            sharedData ??= new ComponentData();
            return sharedData;
        }

        private bool GetBool(string flag) {
            return Bools.IsSet(flag);
        }

        private void OnQuestStepStateChange(QuestStep step,
                QuestStepState from, QuestStepState to) {
            if (step.IsHidden || step.Data.Quest == null) { return; }
            if (to == QuestStepState.Completed) {
                // var icon = step.Data.Quest.IsMain ? IconId.CQuest : IconId.CQuestSide;
                // var msg = $"{GlyphMap.SpriteCheck} {Texts.Green(T.AdjDone)}: {step.Title}";
                // S.Sig.ShowFanfare.Send(msg);

                // var notification = EventNotification.Create(G.Ticks, UDB.Create(UI,
                //     UDBT.IEvent, icon, msg)
                //             .WithTooltip(step.Title)
                //             .WithIconClickFunction(() => UI.ShowQuest(step.Data.Quest)),
                //                 Priority.Second).WithHideAfterSeconds(10);
                // S.Sig.AddEvent.Send(notification);
                if (!isRestoringQuests && QuestUtils.IsStepVisible(step)
                        && (step.Data.State & (QuestState.Active | QuestState.Completed)) != QuestState.Undefined) {
                    Notifications.ShowStepCompleted(step);
                    // The onboarding funnel: tutorial and main chain only, to keep the volume
                    // to what tells where new players stall
                    var def = step.Data.Def;
                    if (def.IsTutorial || def.IsMain) {
                        PlayStats.TrackQuestStepCompleted(def.Id, step.Def.Id);
                    }
                }
            }
            if (step.Data.Quest == TrackedQuest) {
                Notifications.Update();
            }
        }

        private bool SetBool(string flag, bool add) {
            bool changed = false;
            if (add) {
                changed = Bools.Set(flag);
            } else {
                changed = Bools.Unset(flag);
            }
            if (changed) {
                S.Sig.QuestBoolsChanged.Send(flag, add);
            }
            return changed;
        }

        private Quest ForceQuestFor(IQuestProvider prov, QuestDef def, Rng rng) {
            if (prov is ColonistQuestProvider colonistProvider
                    && !ColonistQuestProvider.CanGiveQuest(colonistProvider.BeingData, def)) {
                D.Err("Colonist provider {0} can't give quest {1}", colonistProvider, def);
                return null;
            }
            if (ProviderQuests.TryGetValue(prov, out var existing)) {
                D.Err("Can't force a quest on provider {0} because another quest exists: {1}",
                    prov, existing);
                return null;
            }
            QuestData data;
            try {
                data = QuestData.CreateFor(S, def, rng, prov);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating forced quest {0} for provider {1}", def, prov);
                return null;
            }
            if (data == null) {
                D.Err("Failed creating forced quest {0} for provider {1}", def, prov);
                return null;
            }
            var q = Quest.InstanceFor(S, data);
            if (prov is NPCShip ship) {
                if (ship.IsPlayer) {
                    D.Err("Trying to force a quest for player's ship");
                    return null;
                }
                ship.IsQuestGiver = true;
            }
            // registered after the player-ship rejection, which left the player ship
            // registered as the provider of a quest that was never given
            ProviderQuests[prov] = q;
            prov.QuestId = data.Id;
            return q;
        }

        private bool WantsToOfferQuest(NPCShip ship) {
            if (!ship.IsQuestGiver) { return false; }
            if (ship.NumThrusters < 0) { return false; }
            if (NextNPCShipQuestOfferAt > G.Ticks) { return false; }
            if (ship.NextQuestAvailableAt > G.Ticks) { return false; }
            if (IsActiveQuestProvider(ship)) { return false; }
            return true;
        }

        private bool DelayNextNPCShipQuest(NPCShip ship) {
            if (ship == null) {
                var daysG = Rng.Unseeded.Curve(npcShipQuestDelayDaysGlobalCurve);
                NextNPCShipQuestOfferAt = G.Ticks + (int) (daysG * Consts.TicksPerDay);
                return true;
            }
            // cleanup last completed / failed quest
            if (ProviderQuests.TryGetValue(ship, out var quest)) {
                if (quest.Data.State != QuestState.Undefined && quest.IsEnded) {
                    ProviderQuests.Remove(ship);
                }
            }
            var days = Rng.Unseeded.Curve(npcShipQuestDelayDaysCurve);
            ship.NextQuestAvailableAt = G.Ticks + (int) (days * Consts.TicksPerDay);
            return true;
        }

        private bool IsActiveQuestProvider(IQuestProvider prov) {
            return ProviderQuests.ContainsKey(prov);
        }

        private bool CanOfferColonistQuest(uint mindId, QuestDef def) {
            // Offers waiting for an answer and open quests
            foreach (var prov in ProviderQuests.Keys) {
                if (IsColonist(prov, mindId)) { return false; }
            }
            // Saves from before one quest per colonist hold several open quests for one
            // colonist, and only the first one is in ProviderQuests
            for (int i = ActiveQuests.Count - 1; i >= 0; i--) {
                if (IsColonist(ActiveQuests[i].Data.Provider, mindId)) { return false; }
            }
            if (def.Flags.HasFlag(QuestFlags.OncePerGiver)) {
                // Only accepted quests are listed, so an ignored offer can come again
                for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                    var q = AllPlayerQuests[i];
                    if (q.Data.Def == def && IsColonist(q.Data.Provider, mindId)) {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsColonist(IQuestProvider prov, uint mindId) {
            return prov is ColonistQuestProvider cp && cp.MindId == mindId;
        }

        private bool IsActiveQuestTargetShip(NPCShip ship) {
            if (ship == null) { return false; }
            for (int i = ActiveQuests.Count - 1; i >= 0; i--) {
                var vars = ActiveQuests[i].Data.Variables;
                if (vars == null) { continue; }
                foreach (var v in vars.Values) {
                    if (v is NPCShipQuestVar shipVar && shipVar.Value == ship) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// True while an ACTIVE quest requires the given mind: either as the delivery
        /// passenger stored on the quest's data (PassengerUtils.PassengerMindIdKey) or as
        /// a registered step target's required crew member. Failed/ended quests are not
        /// consulted - they already despawn their passenger through
        /// PassengerWatchdogQuestStep.OnQuestStateChange.
        /// </summary>
        private bool IsMindRequiredByQuest(uint mindId) {
            if (mindId == 0) { return false; }
            for (int i = ActiveQuests.Count - 1; i >= 0; i--) {
                var data = ActiveQuests[i].Data.Data;
                if (data != null
                        && (uint) data.GetInt(PassengerUtils.PassengerMindIdKey, 0) == mindId) {
                    return true;
                }
            }
            foreach (var st in StepTargets.Values) {
                if (st.Step.IsActive && st.RequiredCrewMindId == mindId) {
                    return true;
                }
            }
            return false;
        }

        private Quest GetQuestFor(IQuestProvider provider, bool create, bool makeShipQuestGiver) {
            if (provider == null) { return null; }
            if (ProviderQuests.TryGetValue(provider, out var quest)) {
                if (quest.Data.State == QuestState.Undefined || !quest.IsEnded) {
                    return quest;
                }
            }
            if (!create) { return null; }
            if (provider is NPCShip ship) {
                if (ship.IsPlayer) {
                    D.Err("Trying to get or create a quest for player's ship");
                    return null;
                }
                if (!ship.IsQuestGiver) {
                    // A story/quest-spawned ship must never be turned into a quest
                    // giver, even by a caller that would otherwise force it.
                    if (makeShipQuestGiver && !ship.NoQuests) {
                        ship.IsQuestGiver = true;
                    } else {
                        return null;
                    }
                }
                if (G.Ticks < ship.NextQuestAvailableAt) {
                    return null;
                }
            }
            try {
                quest = gen.CreateFor(provider, QuestFlags.Random, G.Ticks);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed creating random quest for {0}", provider);
            }
            if (quest != null) {
                ProviderQuests.Add(provider, quest);
                provider.QuestId = quest.Data.Id;
            }
            return quest;
        }

        private int GetQuestPackages(QuestData quest, List<QuestPackage> result) {
            int found = 0;
            foreach (var pack in QuestPackages.Values) {
                if (pack.Step.Data == quest) {
                    result.Add(pack);
                    found++;
                }
            }
            return found;
        }

        private QuestPackage FindPackage(uint id) {
            if (QuestPackages.TryGetValue(id, out var res)) {
                return res;
            }
            return null;
        }

        // Removing a provider fails its quest (OnSORemoved) and a missing SO provider drops the
        // quest on load, so the quest lets go of the signal before it is removed
        private void ConsumeProvider(QuestData data, SpaceObject so) {
            ProviderQuests.Remove(so);
            data.Provider = null;
            so.QuestId = 0;
            if (so.Parent != null) {
                SOUtils.RemoveSO(so);
            }
        }

        private void OnQuestStateChange(QuestData data, QuestState before, QuestState after) {
            if ((after & QuestState.Ended) != QuestState.Undefined
                    && data.Provider != null
                    && data.Provider is not NPCShip
                    && ProviderQuests.TryGetValue(data.Provider, out var providerQuest)
                    && providerQuest.Data == data) {
                ProviderQuests.Remove(data.Provider);
            }
            if (before == QuestState.Active && after != QuestState.Active) {
                RemoveFromActive(data.Quest);
                // if (TrackedQuest == null && ActiveQuests.Count > 0) {
                //     TrackQuest(ActiveQuests[0], true);
                // }
            }
            if (after == QuestState.Active) {
                PlayStats.TrackQuestStarted(data.Def.Id);
                if (data.Def.Flags.HasFlag(QuestFlags.ConsumeProvider)
                        && data.Provider is SpaceObject consumed && !consumed.IsShip) {
                    ConsumeProvider(data, consumed);
                }
                if (data.Provider is NPCShip ship) {
                    if (ship.WantsHyperspace) {
                        ship.SetWantsHyperspace(false);
                    }
                    if (ship.IsInHyperspace) {
                        ship.ExitHyperspace();
                    }
                    // ensure that the ship that gave us an active quest won't jump away
                    if (ship.BehaviorRegion?.IsCompatibleWith(ship, ship.Sector) == false) {
                        ship.ChangeBehavior(NPCShipBehaviorScope.Region, false);
                    }
                    if (ship.BehaviorSector?.IsCompatibleWith(ship, ship.Sector) == false) {
                        ship.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
                    }
                }
            } else if (after == QuestState.Completed) {
                PlayStats.TrackQuestCompleted(data.Def.Id);
                Achievements.QuestAchievements.OnQuestCompleted(data.Def.Id);
                Bools.SetAll(data.Def.SetOnComplete);
                var icon = data.Def.IsMain ? GlyphMap.SpriteQuest : GlyphMap.SpriteQuestSide;
                var msg = $"{icon} {Texts.Green(T.QuestEndCompleted)}: {data.Title}";
                if (TrackedQuest == data.Quest) {
                    TrackQuest(TrackedQuest, false);
                }
                // S.Sig.ShowFanfare.Send(msg);
                S.Sys.Log.AddLine(msg);
            } else if (after == QuestState.Failed) {
                PlayStats.TrackQuestFailed(data.Def.Id);
                Bools.SetAll(data.Def.SetOnFail);
                var icon = data.Def.IsMain ? GlyphMap.SpriteQuest : GlyphMap.SpriteQuestSide;
                var msg = $"{icon} {Texts.Red(T.QuestEndFailed)}: {data.Title}";
                if (TrackedQuest == data.Quest) {
                    TrackQuest(TrackedQuest, false);
                }
                // S.Sig.ShowFanfare.Send(msg);
                S.Sys.Log.AddLine(msg);
            } else if (after == QuestState.Rejected) {
                PlayStats.TrackQuestAborted(data.Def.Id);
                if (TrackedQuest == data.Quest) {
                    TrackQuest(TrackedQuest, false);
                }
                Bools.SetAll(data.Def.SetOnFail);
            } else if (after == QuestState.Deleted
                    && before != QuestState.Completed
                    && before != QuestState.Failed
                    && before != QuestState.Rejected) {
                // Deletion likely happened due to pre-check, which means quest
                // is technically completed, though player shouldn't have seen it
                Bools.SetAll(data.Def.SetOnComplete);
                // Validate global interactive quest steps for correct removal
                for (int i = globalInteractiveSteps.Count - 1; i >= 0; i--) {
                    IInteractiveQuestStep gi = globalInteractiveSteps[i];
                    if (gi.Step.Data == data) {
                        D.Err("Quest ended, but interactive quest step was not unregistered! {0} | {1}",
                            gi, data);
                        globalInteractiveSteps.RemoveAt(i);
                    }
                }
            }
            Notifications.UpdateAvailableQuests();
            UpdateSeen(data);
        }

        private bool OpenPackage(QuestPackageComp comp) {
            if (!QuestPackages.TryGetValue(comp.QuestPackageId, out var pack)) {
                D.Err(
                    "Package with id {0} not found, can't open! Obj: {1}; SavedQuestId: {2}; SavedQuestTitle: {3}",
                    comp.QuestPackageId, comp.Entity, comp.QuestId,
                    comp.QuestTitle ?? T.Unknown);
                return false;
            }
            if (pack.State != QuestPackageState.Spawned) {
                D.Err(
                    "Trying to open quest package with state not spawned! {0}; Obj: {1}; SavedQuestId: {2}; SavedQuestTitle: {3}",
                    pack, comp.Entity, comp.QuestId, comp.QuestTitle ?? T.Unknown);
                return false;
            }
            if (pack.Seed == 0) {
                pack.Seed = comp.ContentsSeed != 0
                    ? comp.ContentsSeed
                    : Hashes.Quick(Primes.Prime020, comp.EntityId);
            }
            comp.ContentsSeed = pack.Seed;
            var sb = Caches.StringBuilder3;
            sb.Clear();
            if (pack.StoryEventId != null) {
                var storyDef = StoryEventDef.Get(pack.StoryEventId);
                if (storyDef == null) {
                    D.Err("Failed to find QuestPackage story event by id: {0} | {1}",
                        pack.StoryEventId, pack);
                } else {
                    var data = StoryEventData.CreateFor(storyDef);
                    data.Seed = pack.Seed;
                    data.SetTarget(comp.Obj);
                    if (!S.Query.StartStoryEvent.Ask(data)) {
                        D.Err("Failed to start QuestPackage story event: {0} | {1}",
                            data, pack);
                    }
                }
            }
            if (pack.ResearchDefId != null) {
                var def = The.Defs.Get(pack.ResearchDefId);
                if (def != null) {
                    var res = S.Research.Get(def.IdH);
                    if (!res.IsUnlocked || !res.IsDiscovered) {
                        if (sb.Length == 0) {
                            sb.Append(T.QuestPackageContained);
                            sb.Append(':');
                        }
                        var path = S.Research.TreePathFor(def);
                        if (!res.IsDiscovered) {
                            S.Research.DiscoverWithPrerequisites(def.IdH);
                        }
                        S.Research.UnlockAndUnbookmark(res);
                        sb.AppendFormat("\n {0} {1}: {2}", GlyphMap.ArrowRight,
                            Texts.Accented(T.ResearchUnlocked), path);
                    }
                }
            }
            if (pack.AddQuestId != null) {
                var qdef = QuestDef.Get(pack.AddQuestId);
                if (qdef == null) {
                    D.Err("Failed to find quest by id {0} for package: {1}", pack.AddQuestId, pack);
                } else {
                    var qdata = QuestData.CreateFor(S, qdef, new Rng(pack.Seed));
                    var q = S.Query.AddQuest.Ask(qdata, false);
                    if (q == null) {
                        D.Err("Failed to start quest {0} for package: {1}", qdata, pack);
                    } else {
                        if (sb.Length == 0) {
                            sb.Append(T.QuestPackageContained);
                            sb.Append(':');
                        }
                        sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight,
                            Texts.Accented(T.NounDatalog));
                        S.UIQuery.ShowQuest.Ask(q);
                    }
                }
            }
            if (pack.Mats != null) {
                if (sb.Length == 0) {
                    sb.Append(T.QuestPackageContained);
                    sb.Append(':');
                }
                foreach (var mat in pack.Mats) {
                    var sp = EntityUtils.SpawnRawMaterial(mat, comp.Obj.PosIdx, 1f);
                    sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight,
                        sp.ObjLinkWithCount());
                }
            }

            if (pack.Money > 0) {
                if (sb.Length == 0) {
                    sb.Append(T.QuestPackageContained);
                    sb.Append(':');
                }
                sb.AppendFormat("\n {0} {1}", GlyphMap.ArrowRight,
                    Texts.Green(Wallet.ToValueString(pack.Money)));
                S.Wallet.Add(pack.Money);
            }

            if (sb.Length == 0) {
                D.Err("Quest Package didn't have any loot defined! {0}", pack);
                return false;
            }

            var ff = sb.ToString();
            The.Dialog.ShowSimple(GraphicsId.ObjContainersQuestPackage, ff);
            S.Sys.Log.AddLine(ff);
            // ff = Texts.Green(ff);

            var stateBefore = pack.State;
            pack.State = QuestPackageState.Opened;
            S.Sig.QuestPackageStateChanged.Send(pack, stateBefore, pack.State);
            if (pack.Flags.HasFlag(QuestPackageFlags.QuestProviderRetaliateOnOpen)) {
                var prov = pack.Step.Data.Provider;
                if (prov is NPCShip ship && ship.SO?.Parent == A.PlayerSector) {
                    ShipHailAction_Insult.Insult(ship);
                }
            }
            // packages outlive their quest: opening one of a rejected or completed quest
            // must not move it into another end state
            if (!pack.Step.Data.IsEnded) {
                if (pack.Flags.HasFlag(QuestPackageFlags.CompleteQuestOnOpen)) {
                    pack.Step.Data.SetState(QuestState.Completed);
                } else if (pack.Flags.HasFlag(QuestPackageFlags.FailQuestOnOpen)) {
                    pack.Step.Data.SetState(QuestState.Failed);
                }
            }
            return true;
        }

        private Entity SpawnPackage(uint id, int pos) {
            if (!QuestPackages.TryGetValue(id, out var pack)) {
                D.Err("Couldn't find quest package by id for spawning: {0} (pos: {1})",
                    id, Pos.String(pos));
                return null;
            }
            if (pack.State != QuestPackageState.Created) {
                D.Err("Trying to spawn a package that isn't created: {0}", pack);
                if (pack.State == QuestPackageState.Undefined) {
                    pack.State = QuestPackageState.Created;
                } else {
                    return null;
                }
            }
            var defId = pack.PackageDefId ?? DefIds.ObjContainersQuestPackage;
            var def = The.Defs.Get(defId);
            if (def == null) {
                D.Err("Failed spawning package by def id: {0} | {1}", defId, pack);
                return null;
            }
            var spawned = S.Objs.Create(EntityUtils.CenterOf(pos), def);
            if (spawned.TryGetComponent<QuestPackageComp>(out var packComp)) {
                packComp.AssignQuestPackage(pack);
            }
            pack.State = QuestPackageState.Spawned;
            S.Sig.QuestPackageStateChanged.Send(pack, QuestPackageState.Created,
                QuestPackageState.Spawned);
            ReloadUI();
            return spawned;
        }

        private void ReloadUI() {
            S.UISig.ReloadQuests.Send();
        }

        private bool AddPackage(QuestPackage pack) {
            if (pack.Id == 0) {
                pack.Id = ++QuestPackage.LastId;
            } else {
                D.Err("Trying to add QuestPackage with existing id! {0}", pack);
            }
            if (pack.Step == null) {
                D.Throw("Step not defined when adding a package! {0}", pack);
            }
            if (pack.State != QuestPackageState.Undefined) {
                D.Err("Trying to add a package that has a state! {0}", pack);
            }
            if (!QuestPackages.TryAdd(pack.Id, pack)) {
                D.Err("Trying to add a QuestPackage that is already added! {0}", pack);
                return false;
            }
            pack.State = QuestPackageState.Created;
            pack.IsRegistered = true;
            S.Sig.QuestPackageStateChanged.Send(pack, QuestPackageState.Undefined,
                QuestPackageState.Created);
            return true;
        }

        private bool DestroyPackage(uint id) {
            if (id == 0) {
                D.Err("Trying to remove a QuestPackage with 0 id!");
                return false;
            }
            if (QuestPackages.TryGetValue(id, out var pack)) {
                pack.IsRegistered = false;
                QuestPackages.Remove(id);
                var stateBefore = pack.State;
                pack.State = QuestPackageState.Destroyed;
                S.Sig.QuestPackageStateChanged.Send(pack, stateBefore,
                    QuestPackageState.Destroyed);
                // destroying the package of an abandoned (Rejected) or completed quest used to
                // fail it: penalties, fail bools and a "quest failed" dialog
                if (pack.Flags.HasFlag(QuestPackageFlags.FailQuestOnDestroy)
                        && !pack.Step.Data.IsEnded) {
                    pack.Step.Data.SetState(QuestState.Failed);
                }
            } else {
                D.Err("Trying to remove a QuestPackage that is not in QuestPackages! Id: {0}", id);
                return false;
            }
            return true;
        }

        /// <summary>
        /// One-shot reconciliation: removes duplicate instances of Tutorial / Main / Unique
        /// quest defs from AllPlayerQuests and ActiveQuests, keeping a single best instance
        /// per def. Defends against save corruption where a quest got given repeatedly (e.g.
        /// the restore re-entrancy bug fixed alongside this - an exception during load kept
        /// re-running RestoreData, which kept re-adding the whole saved quest list). Random /
        /// non-unique Side quests are left untouched since those can legitimately repeat.
        /// Idempotent - safe to call repeatedly, including every load.
        /// </summary>
        public int ValidateNoDuplicateQuests() {
            var byDef = new Dictionary<int, List<Quest>>();
            for (int i = 0; i < AllPlayerQuests.Count; i++) {
                var q = AllPlayerQuests[i];
                var def = q?.Data?.Def;
                if (def == null) { continue; }
                if (!def.IsTutorial && !def.IsMain && !def.IsUnique) { continue; }
                if (!byDef.TryGetValue(def.IdH, out var list)) {
                    byDef[def.IdH] = list = new List<Quest>();
                }
                list.Add(q);
            }
            int removed = 0;
            foreach (var list in byDef.Values) {
                if (list.Count <= 1) { continue; }
                var keep = list[0];
                for (int i = 1; i < list.Count; i++) {
                    if (CompareQuestDupes(list[i], keep) > 0) {
                        keep = list[i];
                    }
                }
                for (int i = 0; i < list.Count; i++) {
                    var q = list[i];
                    if (q == keep) { continue; }
                    RemoveDuplicateQuest(q, keep);
                    removed++;
                }
                // D.Warn("QuestDuplicateValidation: removed {0} duplicate(s) of {1}, kept quest {2}",
                //     list.Count - 1, keep.Data.Def.Id, keep.Data.Id);
            }
            if (removed > 0) {
                D.Warn("QuestDuplicateValidation removed {0} duplicate quest instance(s) total",
                    removed);
            }
            return removed;
        }

        /// <summary>
        /// Ranks two instances of the same quest def to decide which duplicate to keep:
        /// further along (Active > Available > Completed > Failed/Rejected/Deleted > Undefined)
        /// wins; ties broken by step progress, then by lowest Id (the original instance).
        /// Returns &gt;0 if <paramref name="a"/> should be kept over <paramref name="b"/>.
        /// </summary>
        private static int CompareQuestDupes(Quest a, Quest b) {
            int rankA = QuestDupeStateRank(a.Data.State);
            int rankB = QuestDupeStateRank(b.Data.State);
            if (rankA != rankB) { return rankA.CompareTo(rankB); }
            a.GetProgress(out var doneA, out _);
            b.GetProgress(out var doneB, out _);
            if (doneA != doneB) { return doneA.CompareTo(doneB); }
            return b.Data.Id.CompareTo(a.Data.Id);
        }

        private static int QuestDupeStateRank(QuestState state) {
            switch (state) {
                case QuestState.Active: return 5;
                case QuestState.Available: return 4;
                case QuestState.Completed: return 2;
                case QuestState.Failed:
                case QuestState.Rejected:
                case QuestState.Deleted: return 1;
                default: return 0; // Undefined
            }
        }

        private void RemoveDuplicateQuest(Quest q, Quest keep) {
            AllPlayerQuests.Remove(q);
            ActiveQuests.Remove(q);
            var prov = q.Data.Provider;
            if (prov != null && ProviderQuests.TryGetValue(prov, out var provQuest) && provQuest == q) {
                ProviderQuests.Remove(prov);
            }
            if (TrackedQuest == q) {
                TrackedQuest = keep;
            }
            // its step checks subscribed on load like any quest's, and a removed duplicate
            // could still complete steps, grant rewards and set bools; deferred as in
            // RemoveFromActive, since this can run inside a signal dispatch
            S.ActQ.Enqueue(q.Cleanup);
        }

        /// <summary>
        /// One-shot reconciliation: for every AutoAdd quest, if the current Bools say it should
        /// be present (AvailableWhen satisfied) but it is neither done nor present in
        /// AllPlayerQuests, re-give it. Recovers main-line progression stranded when a given
        /// quest failed to deserialize on load and got stuck in Seen (the activator skips defs
        /// already in Seen, so a quest lost that way is never re-added on its own). Idempotent -
        /// safe to call repeatedly, including every load.
        /// </summary>
        public int RecoverStalledChain(StringBuilder report = null) {
            int changes = 0;

            // Pass 1: reconcile completion/fail bools for quests Seen recorded as finished.
            // Defensive - covers the rarer "completed but its SetOnComplete bool is missing
            // from the save" desync, distinct from the main "quest never made it back" case.
            foreach (var kv in Seen) {
                var def = kv.Key;
                if (kv.Value.CompleteCount > 0) {
                    changes += ReapplyMissingBools(def, def.SetOnComplete, "complete", report);
                }
                if (kv.Value.FailCount > 0) {
                    changes += ReapplyMissingBools(def, def.SetOnFail, "fail", report);
                }
            }

            // Pass 2: re-give quests that should be present given the (now reconciled) Bools.
            var diff = S.Difficulty.Level;
            for (int i = 0; i < QuestDef.All.Count; i++) {
                var def = QuestDef.All[i];
                if (!def.Flags.HasFlag(QuestFlags.AutoAdd)) { continue; }
                // mirrors QuestActivator.Tick: checked before AvailableWhen so a removed
                // quest never reaches the scenario-skip branch that sets its bools
                if (QuestBools.ContainsAny(Bools, def.RemoveIf)) {
                    activator.RemoveFromAutoAdd(def);
                    report?.AppendLine($" ~ remove-if {def.Id}");
                    continue;
                }
                if (WasQuestAdded(def)) { continue; }
                if (IsConsideredDone(def)) { continue; }
                if (!QuestBools.ContainsAll(Bools, def.AvailableWhen)) { continue; }
                if (!S.Situation.Matches(def.Require, diff)) { continue; }
                if (!PassesValidators(S, def)) { continue; }
                if (def.ForScenarios != null && !Arrays.Contains(def.ForScenarios, S.Scenario.Id)) {
                    // mirrors QuestActivator.Tick: quest isn't for this scenario, so just
                    // advance the chain instead of giving it
                    if (Bools.SetAll(def.SetOnComplete)) {
                        changes++;
                        report?.AppendLine($" ~ scenario-skip {def.Id}");
                    }
                    activator.RemoveFromAutoAdd(def);
                    continue;
                }
                D.Warn("QuestChainRecovery re-giving stranded quest: {0}", def);
                var qData = QuestData.CreateFor(S, def, S.Rng.Fork());
                var q = S.Query.AddQuest.Ask(qData, false);
                if (q != null) {
                    changes++;
                    report?.AppendLine($" + re-gave {def.Id}");
                }
            }
            if (changes > 0) {
                D.Warn("QuestChainRecovery applied {0} change(s)", changes);
            }
            return changes;
        }

        /// <summary>
        /// World-state gate on top of the bools: an AutoAdd quest with QuestValidators is
        /// neither given nor scenario-skipped until every validator agrees. Shared by the
        /// activator, chain recovery and the chain diagnostic so they cannot disagree.
        /// </summary>
        // Validators gate the offer, but an AutoAdd quest can sit offered for hours. If its
        // premise goes away before the player accepts (the infected corpse was disposed of),
        // accepting would complete it on the spot, so the offer is withdrawn instead.
        // Deleted from a non-ended state sets SetOnComplete like a pre-check does. Limited to
        // AutoAdd: generated quests also use validators, and completing their bools is wrong.
        private bool WithdrawIfNoLongerValid(Quest q) {
            if (!q.IsAvailable) { return false; }
            var def = q.Data.Def;
            if (def.Validators == null || !def.Flags.HasFlag(QuestFlags.AutoAdd)) {
                return false;
            }
            if (PassesValidators(S, def)) { return false; }
            D.Warn("Withdrawing offered quest, its validators no longer pass: {0}", q);
            q.Data.SetState(QuestState.Deleted);
            ReloadUI();
            return true;
        }

        public static bool PassesValidators(GameState s, QuestDef def) {
            if (def.Validators == null) { return true; }
            for (int i = def.Validators.Length - 1; i >= 0; i--) {
                if (!def.Validators[i].CanStartQuest(s, def)) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A def is considered finished either because Seen recorded a completion/failure, or
        /// because its output bool is already set (e.g. it was scenario-skipped without ever
        /// getting a Seen entry).
        /// </summary>
        private bool IsConsideredDone(QuestDef def) {
            if (Seen.TryGetValue(def, out var s) && (s.CompleteCount > 0 || s.FailCount > 0)) {
                return true;
            }
            return def.SetOnComplete != null && QuestBools.ContainsAll(Bools, def.SetOnComplete);
        }

        // Same outcome as the player abandoning every tutorial quest one by one. Quests never
        // offered get a Seen entry like a rejected one, because the activator pool is rebuilt
        // from Seen on load and would offer them again.
        public int SkipTutorials(StringBuilder report = null) {
            // First, since every bool below makes the HUD reveal look again
            SetBool(QuestBoolIds.SkippedTutorial, true);
            int changes = 0;
            for (int i = 0; i < QuestDef.All.Count; i++) {
                var def = QuestDef.All[i];
                if (!def.IsTutorial) { continue; }
                if (HasNonEndedQuest(def)) {
                    for (int j = AllPlayerQuests.Count - 1; j >= 0; j--) {
                        var q = AllPlayerQuests[j];
                        if (q.Data.Def.IdH == def.IdH && !q.IsEnded) {
                            q.Data.SetState(QuestState.Rejected);
                        }
                    }
                    changes++;
                    report?.AppendLine($" - abandoned {def.Id}");
                    continue;
                }
                if (WasQuestAdded(def) || Seen.ContainsKey(def)) { continue; }
                Bools.SetAll(def.SetOnFail);
                activator.RemoveFromAutoAdd(def);
                Seen[def] = new QuestSeenData {
                    DefId = def.Id,
                    LastSeen = G.Ticks,
                };
                changes++;
                report?.AppendLine($" ~ skipped {def.Id}");
            }
            if (changes > 0) {
                // The HUD reveal's first wave waits for an offer that will never come now
                SetBool(QuestBoolIds.TutorialQuestOffered, true);
                ReloadUI();
            }
            return changes;
        }

        // The Skip Tutorial choice on a tutorial quest's Abandon
        private void OnSkipTutorial() {
            SkipTutorials();
        }

        private int ReapplyMissingBools(QuestDef def, QuestBools bools, string kind,
                StringBuilder report) {
            if (bools?.Names == null) { return 0; }
            int n = 0;
            for (int i = 0; i < bools.Names.Count; i++) {
                var name = bools.Names[i];
                if (!Bools.IsSet(name)) {
                    SetBool(name, true);
                    report?.AppendLine($" * {def.Id} ({kind} bool): {name}");
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Read-only diagnostic: describes the live Bools registry and every AutoAdd quest's
        /// status relative to it, ordered by prerequisite depth (how many SetOnComplete steps
        /// separate it from bools already set). Use to spot where progression stalled - look
        /// for "MISSING!" entries, which are exactly what RecoverStalledChain would re-give, and
        /// "DEAD-END!" entries, which RecoverStalledChain cannot fix (see QuestDef.FindDeadEnds -
        /// those need a content/JSON fix, not a save repair).
        /// </summary>
        public void DescribeChain(StringBuilder sb) {
            sb.AppendLine($"Bools[{Bools?.Names?.Count ?? 0}]: {string.Join(", ", Bools?.Names ?? new List<string>())}");
            var deadEnds = QuestDef.FindDeadEnds();
            var deadEndsByDef = new Dictionary<QuestDef, List<string>>();
            foreach (var (def, b) in deadEnds) {
                if (!deadEndsByDef.TryGetValue(def, out var names)) {
                    deadEndsByDef[def] = names = new List<string>();
                }
                names.Add(b);
            }
            if (deadEnds.Count > 0) {
                sb.AppendLine($"Dead ends: {deadEnds.Count} (quest(s) need a bool no quest/code will ever set)");
            }
            var autoAdd = ListPool<QuestDef>.Get();
            foreach (var def in QuestDef.All) {
                if (def.Flags.HasFlag(QuestFlags.AutoAdd)) {
                    autoAdd.Add(def);
                }
            }
            var depth = new Dictionary<QuestDef, int>();
            foreach (var def in autoAdd) {
                ChainDepth(def, depth, new HashSet<QuestDef>());
            }
            autoAdd.Sort((a, b) => depth[a].CompareTo(depth[b]));
            foreach (var def in autoAdd) {
                string status;
                if (IsConsideredDone(def)) {
                    status = "DONE";
                } else if (WasQuestAdded(def)) {
                    status = "ACTIVE/AVAIL";
                } else if (QuestBools.ContainsAll(Bools, def.AvailableWhen)) {
                    status = PassesValidators(S, def) ? "MISSING!" : "LOCKED (validator)";
                } else if (deadEndsByDef.TryGetValue(def, out var unreachable)) {
                    status = $"DEAD-END! (unreachable: {string.Join(",", unreachable)})";
                } else {
                    status = "LOCKED";
                }
                var needs = def.AvailableWhen?.Names != null
                    ? string.Join(",", def.AvailableWhen.Names) : "";
                var sets = def.SetOnComplete?.Names != null
                    ? string.Join(",", def.SetOnComplete.Names) : "";
                sb.AppendLine($"[{status}] {def.Id}  needs: {{{needs}}}  sets: {{{sets}}}");
            }
            ListPool<QuestDef>.Release(autoAdd);
        }

        /// <summary>
        /// Depth = how many SetOnComplete links separate this def from bools already satisfied.
        /// Defs whose AvailableWhen is already met (or empty) are depth 0. Guards against cycles
        /// (shouldn't exist, but a broken/duplicated bool name could otherwise loop forever).
        /// </summary>
        private int ChainDepth(QuestDef def, Dictionary<QuestDef, int> memo, HashSet<QuestDef> visiting) {
            if (memo.TryGetValue(def, out var d)) { return d; }
            if (!visiting.Add(def)) { return memo[def] = 0; } // cycle guard
            int depth = 0;
            if (def.AvailableWhen?.Names != null) {
                foreach (var need in def.AvailableWhen.Names) {
                    if (Bools.IsSet(need)) { continue; }
                    foreach (var producer in QuestDef.All) {
                        if (producer == def || producer.SetOnComplete?.Names == null) { continue; }
                        if (producer.SetOnComplete.Names.Contains(need)) {
                            depth = Math.Max(depth, ChainDepth(producer, memo, visiting) + 1);
                            break;
                        }
                    }
                }
            }
            visiting.Remove(def);
            return memo[def] = depth;
        }

        private void RestoreData() {
            // Defensive: RestoreData must be idempotent. It's scheduled via
            // ExecuteAfterFramesForUI, whose runner only unregisters a hook after it
            // returns without throwing (see Clock.cs) - if anything below throws (e.g. a
            // UI callback triggered by TrackQuest), the hook stays armed and re-fires next
            // frame. Clearing first means a re-run rebuilds from `data` instead of
            // appending on top of itself, which is what previously let a single exception
            // here snowball into thousands of duplicate quests.
            isRestoringQuests = true;
            try {
            AllPlayerQuests.Clear();
            ActiveQuests.Clear();
            ProviderQuests.Clear();
            TrackedQuest = null;
            for (int i = 0; i < data.AllPlayerQuests.Length; i++) {
                var q = data.AllPlayerQuests[i];
                try {
                    if (QuestDataData.TryDeserialize(S, q, out var questData)) {
                        var quest = Quest.InstanceFor(S, questData);
                        AllPlayerQuests.Add(quest);
                        var prov = quest.Data.Provider;
                        // Older saves can hold several open quests from one colonist, and
                        // Add would throw before the quest makes it into ActiveQuests
                        if (!quest.IsEnded && prov != null
                                && ProviderQuests.TryAdd(prov, quest)) {
                            if (prov.QuestId != quest.Data.Id) {
                                D.Err("Quest ID mismatch on provider: {0} != {1} | {2}",
                                    prov.QuestId, quest.Data.Id, prov);
                                prov.QuestId = quest.Data.Id;
                            }
                        }
                        if (quest.IsActive) {
                            ActiveQuests.Add(quest);
                            if (data.TrackedQuestId == quest.Data.Id) {
                                TrackQuest(quest, true);
                            }
                        }
                    }
                } catch (System.Exception ex) {
                    D.LogEx(ex, "Failed to restore quest from data! {0}", q.DefId);
                }
            }
            QuestSeenData.Deserialize(data.Seen, Seen);
            QuestPackageData.Deserialize(S, data.Packages, QuestPackages);
            // Quests can get stranded - given, recorded in Seen, but then lost on load (e.g. a
            // deserialize failure for that specific quest). Seen permanently excludes them from
            // auto-add, so without this they'd never come back. Run before LoadQuests() so the
            // rebuilt auto-add rotation reflects the repaired state.
            ValidateNoDuplicateQuests();
            RecoverStalledChain();
            RemoveOrphanedSignalOffers();
            Notifications.Update();
            // Notifications.UpdateAvailableQuests();
            activator.LoadQuests();
            } catch (System.Exception ex) {
                D.LogEx(ex, "RestoreData failed");
            } finally {
                isRestoringQuests = false;
            }
        }

        // A Signal offer that was never accepted isn't persisted (only AllPlayerQuests is,
        // and an unaccepted offer never gets added to it), so after a reload its QuestData
        // is simply gone while the signal SO still carries the stale QuestId. Treat those
        // signals as if the offer had been declined and remove them. Other quests can claim
        // a signal purely as a target (e.g. SectorPOIQuestVar) without it being their
        // Provider, so liveness is checked against all restored quests, not just
        // ProviderQuests.
        private void RemoveOrphanedSignalOffers() {
            var orphans = ListPool<SpaceObject>.Get();
            foreach (var so in S.Universe.ObjectsById.Values) {
                if (so.Type.IdH != SpaceObjectTypeIdH.Signal) { continue; }
                if (so.QuestId == 0) { continue; }
                if (HasLiveQuest(so.QuestId)) { continue; }
                orphans.Add(so);
            }
            foreach (var so in orphans) {
                SOUtils.RemoveSO(so);
            }
            orphans.Clear();
            ListPool<SpaceObject>.Release(orphans);
        }

        private bool HasLiveQuest(uint questId) {
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                var q = AllPlayerQuests[i];
                if (q.Data.Id == questId && !q.IsEnded) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Fires the OnStartTutorialId popup for a quest that has just become the tracked quest.
        /// No-ops during save restore (isRestoringQuests) and when the def has no OnStartTutorialId.
        /// </summary>
        private void TryFireOnStartTutorial(Quest q) {
            if (isRestoringQuests) { return; }
            var def = q?.Data?.Def;
            if (def == null || string.IsNullOrEmpty(def.OnStartTutorialId)) { return; }
            S.UIQuery.ShowTutorialPopup.Ask(def.OnStartTutorialId, def.OnStartTutorialDelay);
        }

        private bool TrackQuest(Quest quest, bool track) {
            D.Warn("Track quest: {0} => {1}", track, quest);
            if (track) {
                if (TrackedQuest == quest) {
                    return false;
                }
                TrackedQuest = quest;
                TryFireOnStartTutorial(quest);
            } else {
                if (TrackedQuest != quest) {
                    return false;
                }
                TrackedQuest = ActiveQuests.Count > 0 ? ActiveQuests[0] : null;
                TryFireOnStartTutorial(TrackedQuest);
            }
            ReloadUI();
            return true;
        }

        private FrugalArray<QuestStepTarget> GetStepTargets(IQuestProvider prov) {
            D.Warn("Getting step target: {0}. {1}", prov, StepTargets.Count);
            FrugalArray<QuestStepTarget> res = default;
            if (prov is SpaceObject so && so.IsShip) {
                D.Err("Getting quest step targets for Ship.SO. Use Ship as a provider instead! {0}", prov);
                prov = so.Ship;
            }
            foreach (var st in StepTargets.Values) {
                if (st.Target == prov) {
                    D.Warn("Found step target: {0}. {1}", prov, st);
                    res.Add(st);
                }
            }
            D.Warn("No step target: {0}", prov);
            return res;
        }

        private bool RegisterStepTarget(QuestStepTarget target, bool register) {
            D.Warn("Registering step target: {0} | {1}", target, register);
            if (register) {
                if (StepTargets.TryAdd(target.Step, target)) {
                    target.IsRegistered = true;
                    return true;
                }
                D.Err("Trying to register a duplicate QuestStepTarget for step: {0} -> {1}",
                    target.Step, target);
            } else {
                if (StepTargets.Remove(target.Step)) {
                    target.IsRegistered = false;
                    return true;
                }
                D.Err("Trying to unregister a QuestStepTarget that wasn't registered: {0} -> {1}",
                    target.Step, target);
            }
            return false;
        }
        private Quest RegisterAvailableQuest(QuestData data, bool add) {
            if (data == null) {
                D.Err("Trying to make null QuestData available!: {0}", add);
                return null;
            }
            if (add && data.Def.IsUnique) {
                if (WasQuestAdded(data.Def)) {
                    D.Err("Trying to make a unique quest available that was available before! {0}", data);
                    return null;
                }
            }
            Quest q = null;
            if (add) {
                q = Quest.InstanceFor(S, data);
                q.Data.SetState(QuestState.Available);
                AllPlayerQuests.Add(q);
            } else {
                for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                    var pq = AllPlayerQuests[i];
                    if (pq.Data == data) {
                        q = pq;
                        AllPlayerQuests.RemoveAt(i);
                        break;
                    }
                }
            }
            ReloadUI();
            // Notifications.UpdateAvailableQuests();
            return q;
        }
        private bool AcceptQuest(Quest q) {
            var data = q.Data;
            if (data.Def.IsUnique) {
                if (WasQuestAdded(data.Def)) {
                    D.Err("Trying to activate a unique quest that was activated before! {0}", data);
                    return false;
                }
            }
            AllPlayerQuests.Add(q);
            DoActivateQuest(q.Data, q);
            return true;
        }
        private Quest AddQuest(QuestData data, bool activate) {
            if (data == null) {
                D.Err("Trying to activate null QuestData!");
                return null;
            }
            Quest q = data.Quest;
            if (data.Quest == null) {
                if (data.Def.IsUnique) {
                    if (WasQuestAdded(data.Def)) {
                        D.Err("Trying to activate a unique quest that was activated before! {0}", data);
                        return null;
                    }
                } else if (data.Def.Flags.HasFlag(QuestFlags.AutoAdd) && HasNonEndedQuest(data.Def)) {
                    // Defense in depth: an AutoAdd (tutorial/main) quest should never be given
                    // again while a prior instance of it is still ongoing. Guards against the
                    // activator's auto-add rotation re-offering a quest if Seen/AllPlayerQuests
                    // ever desync (e.g. an exception during quest restore).
                    D.Err("Trying to auto-add a quest that is already present and not ended! {0}", data);
                    activator.RemoveFromAutoAdd(data.Def);
                    return null;
                }
                q = Quest.InstanceFor(S, data);
                if (q == null) {
                    S.Sig.ShowErrorMessage.Send(
                        "Failed activating the quest, please report a bug");
                    return null;
                }
                if (q.Data.IsEnded) {
                    // A step pre-check in QuestData.CreateFor already completed, failed or
                    // deleted the quest: its objective was met before it could be offered
                    // (an intact ship already has what a tutorial asks for). Its bools are
                    // set and Seen has it, and the player never saw it, so it is not listed.
                    // It has to leave the auto-add pool here - the def is normally removed
                    // further down, and the activator would otherwise recreate it every
                    // rotation.
                    D.Warn("Quest ended on instantiate, possibly due to pre-check: {0}", q);
                    if (data.Def.Flags.HasFlag(QuestFlags.AutoAdd)) {
                        activator.RemoveFromAutoAdd(data.Def);
                    }
                    return null;
                }
                AllPlayerQuests.Add(q);
                if (data.Def.Flags.HasFlag(QuestFlags.AutoAdd)) {
                    activator.RemoveFromAutoAdd(data.Def);
                }
            } else {
                // In case quest wasn't added yet, like in QuestHub quests
                AllPlayerQuests.AddUniqueInefficient(q);
                // Accepting while paused right after the premise went away (the corpse was
                // disposed of on the tick before) would otherwise complete it on the spot
                if (activate && WithdrawIfNoLongerValid(q)) {
                    return null;
                }
            }
            if (activate) {
                DoActivateQuest(data, q);
            } else {
                MakeQuestAvailable(data, q);
            }
            return q;
        }

        private void MakeQuestAvailable(QuestData data, Quest q) {
            if (data.State == QuestState.Undefined) {
                data.SetState(QuestState.Available);
            } else {
                D.Err("Can't make a quest available - it's already of state: {0} | {1}",
                    data.State, data);
                return;
            }
            ReloadUI();
            // Notifications.UpdateAvailableQuests();
            D.Warn("Making quest available: {0}", q);
        }

        public bool UpdateSeen(QuestData data) {
            if (!Seen.TryGetValue(data.Def, out var seen)) {
                seen = new QuestSeenData();
                seen.DefId = data.Def.Id;
            }
            switch (data.State) {
                case QuestState.Undefined:
                case QuestState.Available: {
                    seen.LastSeen = G.Ticks;
                    break;
                }
                case QuestState.Active: {
                    seen.LastSeen = G.Ticks;
                    seen.LastStart = G.Ticks;
                    seen.StartCount++;
                    break;
                }
                case QuestState.Failed: {
                    seen.LastSeen = G.Ticks;
                    seen.FailCount++;
                    break;
                }
                case QuestState.Completed: {
                    seen.LastSeen = G.Ticks;
                    seen.CompleteCount++;
                    break;
                }
            }
            Seen[data.Def] = seen;
            return true;
        }

        // Half-life for variant "seen lately" memory.
        // After one year of in-game time a count of 1 decays to 0.5, so old
        // views gradually stop penalizing a variant.
        private static readonly long VariantSeenHalfLifeTicks = Consts.TicksPerYear;

        private static float DecayedCount(float count, long lastTick, long now) {
            if (count <= 0f) { return 0f; }
            long dt = now - lastTick;
            if (dt <= 0L) { return count; }
            return count * MathF.Pow(0.5f, dt / (float) VariantSeenHalfLifeTicks);
        }

        /// <summary>
        /// Read-only: returns a gently-weighted variant index for <paramref name="def"/>
        /// so less-recently-seen variants are more likely. Returns -1 if def has no TitleKeys.
        /// Does NOT mutate any state.
        /// </summary>
        private int PickQuestVariantIndex(QuestDef def, Rng rng) {
            if (Arrays.IsEmpty(def.TitleKeys)) { return -1; }
            int n = def.TitleKeys.Length;
            float[] counts = null;
            long[] ticks = null;
            if (Seen.TryGetValue(def, out var seen)) {
                counts = seen.VariantSeen;
                ticks = seen.VariantSeenTick;
            }
            long now = G.Ticks;
            float total = 0f;
            for (int i = 0; i < n; i++) {
                float c = (counts != null && i < counts.Length) ? counts[i] : 0f;
                long t = (ticks != null && i < ticks.Length) ? ticks[i] : 0L;
                total += 1f / (1f + DecayedCount(c, t, now));
            }
            float r = rng.Range(0f, total);
            for (int i = 0; i < n; i++) {
                float c = (counts != null && i < counts.Length) ? counts[i] : 0f;
                long t = (ticks != null && i < ticks.Length) ? ticks[i] : 0L;
                float w = 1f / (1f + DecayedCount(c, t, now));
                if (r < w) { return i; }
                r -= w;
            }
            return n - 1; // fallback (floating-point rounding)
        }

        /// <summary>
        /// Write: records that <paramref name="index"/> was seen for <paramref name="def"/>,
        /// applying time-decay to the stored count before adding 1.
        /// </summary>
        private bool MarkQuestVariantSeen(QuestDef def, int index) {
            if (Arrays.IsEmpty(def.TitleKeys)) { return false; }
            int n = def.TitleKeys.Length;
            if (index < 0 || index >= n) {
                D.Err("Variant index out of range when marking seen: {0}/{1} | {2}",
                    index, n, def);
                return false;
            }
            if (!Seen.TryGetValue(def, out var seen)) {
                seen = new QuestSeenData { DefId = def.Id };
            }
            seen.VariantSeen = EnsureFloatLen(seen.VariantSeen, n);
            seen.VariantSeenTick = EnsureLongLen(seen.VariantSeenTick, n);
            long now = G.Ticks;
            seen.VariantSeen[index] =
                DecayedCount(seen.VariantSeen[index], seen.VariantSeenTick[index], now) + 1f;
            seen.VariantSeenTick[index] = now;
            seen.LastSeen = now;
            Seen[def] = seen;
            return true;
        }

        private static float[] EnsureFloatLen(float[] arr, int len) {
            if (arr != null && arr.Length == len) { return arr; }
            var next = new float[len];
            if (arr != null) { Array.Copy(arr, next, Math.Min(arr.Length, len)); }
            return next;
        }

        private static long[] EnsureLongLen(long[] arr, int len) {
            if (arr != null && arr.Length == len) { return arr; }
            var next = new long[len];
            if (arr != null) { Array.Copy(arr, next, Math.Min(arr.Length, len)); }
            return next;
        }

        private void DoActivateQuest(QuestData data, Quest q) {
            data.SetState(QuestState.Active);
            if (!data.IsActive) {
                // A step's pre-check completed/failed/deleted the quest re-entrantly
                // from inside SetState above (e.g. its CompletesQuest step was already
                // satisfied before the quest got accepted) - don't track it as active.
                ReloadUI();
                return;
            }
            ActiveQuests.Add(q);
            if (TrackedQuest == null || !TrackedQuest.IsActive) {
                TrackedQuest = q;
                TryFireOnStartTutorial(q);
            }
            ReloadUI();
            D.Warn("Activating quest: {0}", q);
        }

        private void OnTick(long ticks) {
            activator.Tick();
            if (TrackedQuest != null && !TrackedQuest.IsActive) {
                TrackQuest(TrackedQuest, false);
            }
            for (int i = ActiveQuests.Count - 1; i >= 0; i--) {
                Quest q = ActiveQuests[i];
                if (!q.IsActive) {
                    RemoveFromActive(q);
                    continue;
                }
                q.Tick(ticks);
            }
            Notifications.SyncStepTitles();
            var checkValidators = ticks % 10 == 7;
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = AllPlayerQuests[i];
                if (!q.IsAvailable) { continue; }
                if (checkValidators && WithdrawIfNoLongerValid(q)) { continue; }
                var steps = q.Steps;
                for (int i1 = steps.Length - 1; i1 >= 0; i1--) {
                    QuestStep step = steps[i1];
                    // a completed step must not turn into a failed one
                    if (step.IsEnded) { continue; }
                    if (step.Deadline > 0 && ticks > step.Deadline) {
                        // this step has failed!
                        D.Warn("Failing available quest step: {0}", step);
                        step.SetState(QuestStepState.Failed);
                    } else if (step.Flags.HasFlag(QuestStepFlags.TickAvailable)) {
                        step.Tick(ticks);
                    }
                }
            }
        }



        private void RemoveFromActive(Quest q) {
            if (ActiveQuests.Remove(q)) {
                // if (TrackedQuest == q) {
                //     TrackQuest(q, false);
                // }
                // Cleans up every step of the quest at once, which unsubscribes
                // several signal listeners. This is reached from inside a signal
                // dispatch (a step listener completing the quest), so it has to
                // be deferred - see QuestStep.SetState.
                S.ActQ.Enqueue(q.Cleanup);
                ReloadUI();
            }
        }

        public bool WasQuestAdded(QuestDef def) {
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = AllPlayerQuests[i];
                if (q.Data.Def.IdH == def.IdH) {
                    return true;
                }
            }
            return false;
        }

        private bool HasNonEndedQuest(QuestDef def) {
            for (int i = AllPlayerQuests.Count - 1; i >= 0; i--) {
                Quest q = AllPlayerQuests[i];
                if (q.Data.Def.IdH == def.IdH && !q.IsEnded) {
                    return true;
                }
            }
            return false;
        }

        public override void Unload() {
        }

        public string GetName() {
            return Id;
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            var qd = sd.Quests ??= new QuestSysData();
            qd.LastQuestId = QuestData.LastId;
            qd.TrackedQuestId = TrackedQuest?.Data.Id ?? 0;
            qd.AllPlayerQuests = QuestDataData.Serialize(AllPlayerQuests);
            qd.Packages = QuestPackageData.Serialize(QuestPackages);
            qd.Seen = QuestSeenData.Serialize(Seen);
            qd.SharedData = sharedData;
            qd.Bools = Bools;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd.Quests != null) {
                QuestData.LastId = sd.Quests.LastQuestId;
                data = sd.Quests;
                Bools = sd.Quests.Bools;
                sharedData = sd.Quests.SharedData;
            }
        }
    }
}
