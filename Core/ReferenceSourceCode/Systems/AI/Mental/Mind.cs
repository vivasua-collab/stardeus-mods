using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.AI.Combat;
using Game.Systems.Backstories;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.Systems.Stats;
using Game.Systems.Traits;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Mental {
    public sealed class Mind {
        public static uint LastId;
        public uint Id;
        public BeingData Owner;
        public FactionDef Faction;

        public FactionDef OriginalFaction;
        // Where a restrained being goes back to once the fight ends. AntiPlayerTemp otherwise
        // hands everyone back to Player, which would turn a guest or a chipped raider into crew.
        public FactionDef ReturnFaction;
        /// <summary>
        /// Special non-hostile colony role (guest passenger, future prisoner/visitor, …).
        /// Lives on the Mind so it follows identity: a mind transfer (brain transplant) carries
        /// the role to the new body, and death removes it with the mind.
        /// <seealso cref="ColonyRole"/>
        /// </summary>
        public ColonyRole Role;
        public BeingName Name;
        public Jobs Jobs;
        public Species Species;
        public MentalHealth Health;
        public float Age;
        public long BirthTick;
        public CombatUnitConfig CombatConfig;
        public Experience Experience;
        public BeingTraits Traits;
        public BeingSex Sex;
        public BeingSex[] AttractedToSex;
        public BeingBonds Bonds;
        public string LastCauseOfDeath;
        public long LastTimeOfDeath;
        // Tick a Deserter (see ColonyRole) abandoned the ship. 0 if never deserted.
        public long DesertedTick;
        // Tick this being was sent into stasis (see ColonyRole.InStasis). 0 if never sent.
        public long StasisSentTick;
        // Entity id of the StasisArray holding this mind's body (see ManifestSys). 0 when not
        // in a pod, or for a sleeper from a save that predates per-array binding.
        public int StasisArrayId;
        // Whether this mind came from a stasis manifest, and if it was ever woken.
        public ManifestStatus ManifestStatus;
        public string LinkToShortName => Owner == null ? Name.ShortName :
            Texts.LinkToMind(this, Name.ShortName);
        public string FavColorId;
        /// <summary>
        /// Per-being equipment policy override. null means this being inherits from
        /// its species, or from the global policy when the species has none. Lives on
        /// the Mind like Jobs so it follows identity through a mind transfer.
        /// </summary>
        public EquipmentPolicy EquipmentPolicy;
        // Lives on the Mind so the colonist's trust in the AI follows a mind transfer
        public readonly Trust Trust;
        public bool IsBlank => Faction?.IsBlank != false;

        public Mind() {
            Trust = new Trust(this);
        }

        public static int AgeYears(float age) {
            return Mathf.Max(0, Mathf.FloorToInt(age));
        }

        public static string AgeText(float age) {
            return T.YearsAbbr_V1.F(Units.Num(AgeYears(age)));
        }

        /// <summary>
        /// We want to collect all mental stat values when we remove the brain / core
        /// from the body. Then set those values back when we replace the body
        /// </summary>
        public Dictionary<int, float> MentalStats;

        /// <summary>
        /// For empty minds, so we know who exactly the body belonged to
        /// </summary>
        public uint PreviousId;
        /// <summary>
        /// ObjData id that holds this mind. 0 if it's not inside an obj.
        /// Mostly for mourning, it doesn't matter if multiple objects link to this.
        /// We prefer to link to ashes.
        /// </summary>
        public uint ObjId;
        /// <summary>
        /// BeingData id that held this mind before it was removed / swapped.
        /// </summary>
        public uint FormerOwnerId;

        private string combatLogName;
        private CombatLogSide combatLogSide;
        private enum CombatLogSide : byte { Crew, Guest, Enemy }
        // Rebuilt when the side changes, not only on SetFaction: a guest that defects or an
        // obey chip changes the side without touching Mind.Faction.
        public string CombatLogName {
            get {
                var side = CurrentCombatLogSide();
                if (combatLogName == null || side != combatLogSide) {
                    combatLogSide = side;
                    if (side == CombatLogSide.Crew) {
                        combatLogName = Texts.LinkToMind(this, Texts.Green(Name.ShortName));
                    } else if (side == CombatLogSide.Guest) {
                        var fac = ReturnFaction ?? Faction;
                        combatLogName = fac.IsInternal || fac.IsBlank
                            ? Texts.LinkToMind(this, Name.ShortName)
                            : $"{fac.Glyph} {Texts.LinkToMind(this, Name.ShortName)}";
                    } else {
                        combatLogName = $"{Texts.LinkToMind(this, Texts.Red(Name.ShortName))} ({Texts.Yellow(T.NounEnemy)})";
                    }
                }
                return combatLogName;
            }
        }

        // A guest, restrained or not, is never the enemy, and neither is one of our own
        // being restrained (AntiPlayerTemp)
        private CombatLogSide CurrentCombatLogSide() {
            if (Owner == null) { return CombatLogSide.Crew; }
            if (Owner.IsGuest) { return CombatLogSide.Guest; }
            if (Owner.EffectiveFaction.IsPlayer || Faction?.IdH == FactionIdH.AntiPlayerTemp) {
                return CombatLogSide.Crew;
            }
            return CombatLogSide.Enemy;
        }

        public void OnDeath(string cause, long ticks) {
            if (cause == null) {
                D.Warn("Cause of death is null! {0}", this);
                cause = T.Unknown;
            }
            // D.Warn("Setting cause of death: {0} ({1}) -> {2} ({3}) | {4}",
            //     LastCauseOfDeath, Units.TicksToTime(LastTimeOfDeath),
            //     cause, Units.TicksToTime(ticks), this);
            if (Owner != null) {
                Owner.Body.SetDeathCause(cause, ticks);
            }
            LastCauseOfDeath = cause;
            LastTimeOfDeath = ticks;
        }

        public static Mind Blank(BeingName name, Species species) {
            var m = new Mind();
            m.Name = name;
            m.Species = species;
            m.CombatConfig = species.DefaultCombatConfig;
            m.Jobs = Jobs.BlankFor(m);
            m.Experience = Experience.BlankFor(m);
            m.Traits = BeingTraits.BlankFor(m);
            m.SetFaction(FactionDef.Blank);
            return m;
        }
        public uint EnsureId() {
            if (Id == 0) {
                Id = ++LastId;
            }
            return Id;
        }

        public void SetOwner(BeingData owner) {
            // Persistent mental stats live on the being while a mind is in it, so the mind
            // stepping out has to snapshot them here or it comes back wearing the
            // personality of whoever used the body in the meantime.
            var leaving = owner.Mind;
            if (leaving != null && leaving != this && leaving.Owner == owner) {
                leaving.SaveMentalStats();
            }
            // Mental needs, species abilities and species stats belong to the mind, so swapping
            // the mind in a body has to rebuild what the mind holding slot contributes. Without
            // it the previous occupant needs - Fun above all - outlive the mind that earned them,
            // and the being ends up wanting to have fun while having no mental health to feel it
            // with, or reading and cooking with a crawler brain.
            // The teardown has to straddle the swap: stat modifiers are matched by the objects
            // cached on the Species, so they can only be removed while the mind that applied
            // them is still the one in the body.
            var slot = MindSourceSlotFor(owner);
            slot?.RemoveMindSources();
            Owner = owner;
            Owner.Mind = this;
            Owner.MindId = 0;
            Trust.Sync();
            Owner.UI?.ResetBio();
            // skill-sourced stats (MeleeDodge) now read this mind's levels
            SkillStatSource.RefreshAll(Owner);
            // the job UIs hold the leaving mind's job slots
            if (leaving != this && owner.Being != null) {
                A.S.Sig.BeingJobsChanged.Send(owner);
            }
            if (slot == null) { return; }
            slot.AddMindSources();
            // The arriving mind brings its own persistent mental stats. Without this it would
            // wear the personality of whoever used this body before it. After the rebuild, so
            // that the need gated ones are judged against the needs this mind actually has.
            // Deliberately not done in AINeedMentalHealth.Grant: that also runs on load and
            // after a resurrection, where the being own saved stats are the fresher copy.
            LoadMentalStats();
            Owner.AfterAbilitiesChanged();
            Owner.AfterNeedsChanged(The.R.State);
            Owner.SyncMentalHealthWithNeeds();
        }

        /// <summary>
        /// The slot to rebuild around a mind swap, or null when there is nothing to do:
        /// BeingsSys.LinkBeingMinds links minds before Body.AfterLoad has added the sources, an
        /// unspawned being never gets them at all (AddNeedSources refuses to add for one), and an
        /// empty slot contributes nothing on behalf of any mind. That last one is not just an
        /// optimisation: TransferMindAfterPartRemoval parks a Mind.Blank on a body whose part has
        /// just been pulled, and refreshing there would hand that blank mind the job slots the
        /// leftover body abilities enable, when the whole point of it is to be empty.
        /// </summary>
        private static BodyPartSlot MindSourceSlotFor(BeingData owner) {
            var slot = owner.Body?.MindSlot;
            if (slot == null || !slot.IsLoaded || owner.Being == null) { return null; }
            if (slot.InstalledDef == null) { return null; }
            return slot;
        }
        public void AfterSetFaction(bool changed) {
            // Trust only counts while the mind is Player
            Trust.Sync();
            var b = Owner?.Being;
            if (b == null) { return; }
            // could be loading a modded being and the species doesn't exist anymore
            if (Species == null) { return; }
            if (changed) {
                combatLogName = null;
                if (Owner != null) {
                    Owner.GroupId = 0;
                }
            }
            b.Transmit(null, MsgIdH.FactionChanged);
            if (Faction.IsPlayer && !Species.IsBiological) {
                Owner.StatsBeing.AIAlignment.TryAddModifier(friendlyBot, true);
            } else {
                Owner.StatsBeing.AIAlignment.RemoveModifier(friendlyBot, false);
            }
            // Restraint (AntiPlayerTemp) is temporary: the colonist comes back to Player with
            // the trust they had, and BeingData.EffectiveFaction ignores alignment meanwhile
            if (!Faction.IsPlayer && Faction.IdH != FactionIdH.AntiPlayerTemp) {
                // make sure that sudden faction change won't keep the being controllable
                Owner.StatsBeing.AIAlignment.UpdateBase(0f);
                Owner.StatsBeing.AIAlignment.Apply();
            }
            b.Motion.UpdatePermissions();
            b.Name.CheckState();
            if (b.S.Query.IsSelected.Ask(b)) {
                UIUtils.RefreshSelectedEntityUI();
            }
        }

        private static StatModifier _friendlyBot;
        private static StatModifier friendlyBot => _friendlyBot
            ??= StatModifier.Sets(1f, $"{T.NounMind}: {T.SpeciesTypeRobotic}");

        public void SetFaction(FactionDef fac, bool isLoading = false) {
            // Only treat this as a "real" faction change once the being is actually spawned.
            // During generation, minds are first defaulted to Player before their real
            // (e.g. enemy) faction is assigned - we don't want that placeholder remembered
            // as OriginalFaction, or enemies would be misidentified as ex-player for things
            // like ashes labeling (see Mind.CreateAshes).
            if (Faction != null && OriginalFaction == null && Owner?.Being != null) {
                OriginalFaction = Faction;
            }
            bool changed = fac != Faction;
            Faction = fac;
            if (!isLoading) {
                AfterSetFaction(changed);
            }
        }

        public override string ToString() {
            return $"Mind[{Id}|{Name}|{Faction}]";
        }

        public void SaveMentalStats() {
            if (MentalStats == null) {
                MentalStats = new();
            } else {
                MentalStats.Clear();
            }
            foreach (var mentalStat in Owner.StatsBeing.PersistentMentalStats) {
                MentalStats[mentalStat.Id] = mentalStat.BaseValue;
            }
        }

        public void LoadMentalStats() {
            if (MentalStats == null) { return; }
            foreach (var mentalStat in Owner.StatsBeing.PersistentMentalStats) {
                if (MentalStats.TryGetValue(mentalStat.Id, out var val)) {
                    MarkMentalStatUsed(mentalStat);
                    mentalStat.UpdateBase(val);
                    mentalStat.Apply();
                } else if (TryGetMissingMentalStatDefault(mentalStat.Id, out var def)) {
                    // Old saves predate the breakdown-threshold stats; default them so
                    // existing beings get sane thresholds instead of 0 (never breaking).
                    MarkMentalStatUsed(mentalStat);
                    mentalStat.UpdateBase(def);
                    mentalStat.Apply();
                }
            }
        }

        /// <summary>
        /// Restoring a value must not resurrect a stat the being has no need for. Fun and
        /// DepressionTendency are persistent mental stats AND need gated, and whether they are
        /// shown at all belongs to StatsBeing.ApplyNeeds - a crawler mind carries a saved Fun
        /// value around without the Fun need to go with it.
        /// </summary>
        private void MarkMentalStatUsed(Stat mentalStat) {
            if (mentalStat.RequiredNeedId != 0 && !Owner.HasNeed(mentalStat.RequiredNeedId)) {
                return;
            }
            mentalStat.IsUnused = false;
        }

        // Breakdown-threshold stats added after the original mental-stat set. Missing ids
        // in older saves fall back to their tunable defaults.
        private static bool TryGetMissingMentalStatDefault(int id, out float def) {
            if (id == StatIdH.MentalBreakdownLightThreshold) {
                def = Tunables.BreakdownLightThreshold; return true;
            }
            if (id == StatIdH.MentalBreakdownMediumThreshold) {
                def = Tunables.BreakdownMediumThreshold; return true;
            }
            if (id == StatIdH.MentalBreakdownHeavyThreshold) {
                def = Tunables.BreakdownHeavyThreshold; return true;
            }
            def = 0f;
            return false;
        }
        public const int GenMentalStatsLength = 9;
        /// <summary>
        /// How far a being's own resting sanity may sit from its difficulty's. MentalHealth
        /// also lifts saved baselines to the bottom of this range when a difficulty is retuned.
        /// </summary>
        public const float SanityBaselineSpread = 0.03f;
        public static (int, float, float)[] GenMentalStats => new [] {
            (StatIdH.AIAlignment, 0.4f, 0.8f),
            (StatIdH.Fun, 0.5f, 0.9f),
            (StatIdH.Sanity, A.S.Difficulty.SanityBaseline - 0.05f, A.S.Difficulty.SanityBaseline + 0.05f),
            (StatIdH.SanityBaseline, A.S.Difficulty.SanityBaseline - SanityBaselineSpread, A.S.Difficulty.SanityBaseline + SanityBaselineSpread),
            (StatIdH.SanityTarget, A.S.Difficulty.SanityBaseline - 0.1f, A.S.Difficulty.SanityBaseline + 0.1f),
            (StatIdH.DepressionTendency, -0.2f, 0.2f),
            (StatIdH.MentalBreakdownLightThreshold, Tunables.BreakdownLightThreshold, Tunables.BreakdownLightThreshold),
            (StatIdH.MentalBreakdownMediumThreshold, Tunables.BreakdownMediumThreshold, Tunables.BreakdownMediumThreshold),
            (StatIdH.MentalBreakdownHeavyThreshold, Tunables.BreakdownHeavyThreshold, Tunables.BreakdownHeavyThreshold),
        };

        public void GenerateMentalStats(Rng rng) {
            if (MentalStats == null) {
                MentalStats = new();
            } else {
                D.Err("Generating mental stats when previous was not null! {0} -> {1}",
                    this, D.Dump(MentalStats));
                return;
            }
            foreach (var s in GenMentalStats) {
                MentalStats[s.Item1] = rng.Range(s.Item2, s.Item3);
            }
        }

        public static void RegisterIfNecessary(GameState s, Mind mind) {
            if (mind.Id != 0) { return; }
            if (mind.IsBlank) { return; }
            // we want robot cores to be resolvable by mind id too
            // if (mind.Health == null) { return; }
            s.Query.RegisterMind.Ask(mind);
        }

        public string DeceasedInfo(string deathCause, long deathTime) {
            var sb = Caches.StringBuilder2;
            sb.Clear();
            var name = Name.FullNameOrNameless;
            sb.Append(Texts.LinkToMind(this, Texts.LinkColor(name)));
            sb.AppendFormat("\n   {0} ", GlyphMap.ArrowRight);
            sb.Append(GetObituary(Texts.Yellow(AgeText(Age))));

            // sb.AppendFormat("\n   {0} {1}, {2}: {3}", GlyphMap.ArrowRight,
            //     Texts.Yellow(Species.Name), T.Age, Texts.Yellow(Units.Num(Age)));
            if (deathCause != null) {
                sb.AppendFormat("\n   {0} {1}", GlyphMap.ArrowRight,
                    T.DeathCause_V1.F(deathCause));
            }
            if (deathTime != 0) {
                sb.AppendFormat("\n   {0} {1}: {2}", GlyphMap.ArrowRight,
                    T.TimeOfDeath, Units.TicksAgo((int) (G.Ticks - deathTime)));
            }
            return sb.ToString();
        }

        public static void LinkToObj(Mind mind, ObjData obj) {
            if (mind == null) {
                D.Err("Trying to link null mind to obj {0}", obj);
                return;
            }
            if (obj == null) {
                D.Err("Trying to link mind {0} to null obj!", mind);
                return;
            }
            if (mind.Id == 0) {
                D.Err("Linking unregistered mind: {0} to obj: {1}", mind, obj);
                A.S.Query.RegisterMind.Ask(mind);
            }
            if (obj.Mind != null) {
                D.Err("Trying to link a mind {0} to object {1} which already has a mind linked: {2}",
                    mind, obj, obj.Mind);
            }
            obj.SetMind(mind);
        }
        public static void UnlinkFromObj(Mind mind, ObjData obj) {
            if (mind == null) {
                D.Err("Trying to unlink null mind from obj: {0}", obj);
                return;
            }
            if (obj == null) {
                D.Err("Trying to unlink mind from null object: {0}", mind);
                return;
            }
            if (obj.Mind != mind) {
                D.Err("Mind was not linked or wrong mind was linked when trying to unlink mind {0} from obj {1}. Linked mind: {2}",
                    mind, obj, obj.Mind);
                return;
            }
            obj.SetMind(null);
            mind.ObjId = 0;
        }

        public static Obj CreateAshes(GameState s, Vector2 pos, Mind mind) {
            if (mind == null) { return null; }
            var ashes = s.Objs.Create(pos, The.Defs.FastGet(DefIdsH.ObjMaterialsAshes));
            if (mind.IsBlank || mind.Species.IsWild) { return ashes; }
            var isPlayer = mind.Faction?.IsPlayer == true
                || mind.Faction?.IsAntiPlayer == true
                || mind.OriginalFaction?.IsPlayer == true;
            if (!isPlayer && mind.Owner?.Body.IsForcedToObey == true) {
                isPlayer = true;
            }
            if (!isPlayer && !s.Prefs.GetBool(Pref.LabelEnemyAshes,
                        PrefH.LabelEnemyAshes, Consts.LabelEnemyAshesDefault)) {
                return ashes;
            }

            ObjData existingAshes = null;

            if (mind.ObjId > 0) {
                // find existing ashes and forget them, also eject if they are in an urn
                var mindObj = s.Query.FindObjData.Ask(mind.ObjId);
                // if body part is not null, this is a brain / core - don't touch it!
                if (mindObj != null && mindObj.BodyPart == null) {
                    existingAshes = mindObj;
                }
            }
            if (existingAshes == null) {
                var allObjs = s.Query.GetAllObjs.Ask();
                foreach (var obj in allObjs.Values) {
                    if (obj.Mind == mind && obj.BodyPart == null) {
                        // found ashes
                        existingAshes = obj;
                        break;
                    }
                }
            }

            if (existingAshes == null) {
                ashes.Data.SetMind(mind);
                existingAshes = ashes.Data;
                ashes.Pile.IsForbidden = true;
                if (!ashes.Pile.TryTreatAsObj(true)) {
                    // should never happen
                    D.Err("failed creating named ashes that could be treated as obj! {0}", ashes);
                }
            }

            // reassign from removed brain body part
            existingAshes.CustomString = mind.LastCauseOfDeath;
            existingAshes.CustomLong = mind.LastTimeOfDeath;

            // update the storage info
            if (existingAshes.Obj?.TryGetComponent<BeingAshesComp>(out var bashes) == true) {
                bashes.UpdateInfo();
            } else if (existingAshes.StorageId > 0) {
                // ashes in an urn?
                var urn = s.Components.Find<UrnNameComp>(existingAshes.StorageId);
                if (urn != null) {
                    urn.CheckState();
                }
            }

            return ashes;
        }

        public bool MatchesBody(out bool ageOnly) {
            ageOnly = false;
            if (IsBlank) { return true; }
            if (Owner?.Body == null) { return false; }
            bool speciesMatches = Owner.Species == Species;
            bool sexMatches = Owner.Body.Sex == Sex;
            bool ageMatches = AgeYears(Age) == AgeYears(Owner.Body.Age);
            ageOnly = speciesMatches && sexMatches && !ageMatches;
            return speciesMatches && sexMatches && ageMatches;
        }
        public void AddTraits(StringBuilder sb, bool prependComma, bool withoutTooltips) {
            if (Traits == null) { return; }
            if (Arrays.IsEmpty(Traits.Slots)) { return; }
            if (prependComma) {
                sb.Append(", ");
            }
            if (withoutTooltips) {
                sb.AppendJoin(", ", Traits.Slots.Select(t => t.Trait.NameT));
            } else {
                sb.AppendJoin(", ", Traits.Slots.Select(
                    t => Texts.LinkToTraitTooltip(this, t.Trait)));
            }
        }

        public bool AddExperience(StringBuilder sb, bool withoutTooltips) {
            sb.AppendLine();
            if (Experience == null) {
                sb.Append(T.AdjUnremarkable);
                return false;
            }
            if (Arrays.IsEmpty(Experience.Roles)) {
                sb.Append(T.AdjUnremarkable);
                return false;
            }
            if (withoutTooltips) {
                sb.AppendJoin(", ", Experience.Roles.Select(r => r.Value.NameT));
            } else {
                sb.AppendJoin(", ", Experience.Roles.Select(r =>
                    Texts.LinkToExperienceTooltip(this, r.Key, r.Value)));
            }
            return true;
        }
        private string cachedObituary;
        private string cachedObituaryAgeText;

        public void AddMindInfo(StringBuilder sb) {
            // if (sb.Length > 0) {
            //     sb.Append(GlyphMap.BRHR);
            // }
            if (Name.DisplayName == null) {
                Name.UpdateName();
            }
            if (!Faction.IsPlayer && !Faction.IsInternal) {
                sb.Append(Faction.Glyph);
                sb.Append(' ');
                sb.Append(Texts.Red(Faction.NameT));
                sb.Append(' ');
            }
            string ageText = AgeText(Age);
            var spName = Species.Def.CodexLink;
            if (Sex != null) {
                sb.Append(T.PersonaBio_V3.F(spName, Sex.NameT, ageText));
            } else {
                sb.Append(T.PersonaBioSexless_V2.F(spName, ageText));
            }
            var orientation = SexualOrientation.ResolveName(Sex, AttractedToSex, out var isStraight);
            if (!isStraight && orientation != null) {
                sb.AppendFormat(", {0}", orientation);
            }
            bool hadExperience = AddExperience(sb, true);
            AddTraits(sb, hadExperience, true);
            sb.Append(GlyphMap.BRHR);
        }

        // Human Female, 37y (Mind: Human Male, 26y)
        // Attracted to: Male, Female
        // Monk, Homeless, Unemployed,
        // Fast Metabolism, Short Sleeper, Carnivore
        public string GetObituary(string ageText) {
            if (cachedObituary != null && cachedObituaryAgeText == ageText) {
                #if !DEBUG_UDB
                return cachedObituary;
                #endif
            }
            var sb = Caches.StringBuilder;
            sb.Clear();

            if (!Faction.IsPlayer && !Faction.IsInternal) {
                sb.Append(Faction.Glyph);
                sb.Append(' ');
                sb.Append(Texts.LinkToFactionTooltip(Faction));
                sb.Append(' ');
            }

            var spName = Species?.Def?.CodexLink ?? T.Unknown;
            if (Sex != null) {
                sb.Append(T.PersonaBio_V3.F(spName, Sex.NameT, ageText));
            } else {
                sb.Append(T.PersonaBioSexless_V2.F(spName, ageText));
            }
            var orientation = SexualOrientation.ResolveName(Sex, AttractedToSex, out var isStraight);
            if (!isStraight && orientation != null) {
                if (!Arrays.IsEmpty(AttractedToSex)) {
                    var attrs = string.Join(", ", AttractedToSex.Select(v => v.NameT));
                    orientation = Texts.LinkToTooltip(orientation, $"{T.AttractedTo}: {attrs}");
                }
                sb.AppendFormat(", {0}", orientation);
            }
            sb.AppendLine();
            bool hadExperience = AddExperience(sb, false);
            AddTraits(sb, hadExperience, false);
            cachedObituary = sb.ToString();
            cachedObituaryAgeText = ageText;
            return cachedObituary;
        }

        public string DecorateListName(string name) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append(name);
            sb.Append(' ');
            sb.Append(GlyphMap.ArrowRight);
            sb.Append(' ');
            if (Faction.IsPlayer) {
                sb.Append(Texts.Green(Name.ShortName));
            } else if (!Faction.IsBlank) {
                sb.Append(Faction.Glyph);
                sb.Append(' ');
                sb.Append(Texts.Red(Name.ShortName));
            }
            sb.AppendFormat("<color={0}>", Texts.TMPColorGrey);
            sb.Append(" (");
            string ageText = AgeText(Age);
            sb.Append(ageText);
            if (Sex != null) {
                sb.Append(" ");
                sb.Append(Sex.NameT);
                var orientation = SexualOrientation.ResolveName(Sex, AttractedToSex, out var isStraight);
                if (!isStraight && orientation != null) {
                    sb.AppendFormat(", {0}", orientation);
                }
            }
            sb.Append(")</color>");
            return sb.ToString();
        }
    }
}
