#if !PRODUCTION_BUILD
// #define DEBUG_EVENTS
// #define DEBUG_COOLDOWN
// #define DEBUG_POINTS
#endif
using System;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Modifiers;
using Game.Mods;
using Game.Systems.Space;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Story {
    public sealed class StoryContext {
        public StoryContext(GameState s) {
            S = s;
            PrioritizedEvents = new Dictionary<int, long>();
            AspectData = new ComponentData();
        }
        public readonly GameState S;
        public long Ticks;
        public bool IsEveryHour;
        public bool IsEveryDay;
        public bool IsEvery15Min;
        public readonly Queue<StoryEventDef> EventsToConsider = new();
        /// <summary>
        /// This dictionary can survive multiple ticks, so don't use it for hook events or
        /// anything else.
        /// </summary>
        public readonly Dictionary<StoryEventDef, float> PotentialEvents = new();
        public readonly Dictionary<StoryEventDef, float> PotentialHookEvents = new();
        public readonly List<StoryEventData> OngoingEvents = new();
        public readonly List<StoryEventData> PastEvents = new();
        public readonly Dictionary<int, EventSeenData> Seen = new();
        public readonly Dictionary<StoryEventFlags, long> FlagCooldowns = new();
        /// <summary>
        /// Not save/loadable, so each object should recreate a hook if necessary
        /// </summary>
        public readonly List<StoryEventHook> ActiveHooks = new();
        public Rng Rng;
        /// <summary>
        /// key is EventOutcome, value is if the events of this type can happen
        /// </summary>
        public StorySchedule[] Schedule;
        public StoryGenDef Generator;
        public SpaceSector CurrentSector;
        public Dictionary<int, long> PrioritizedEvents;
        public ComponentData AspectData;
        /// <summary>
        /// Transient (not saved): strength multiplier for the raid currently being started
        /// by <see cref="RaidStoryAspect"/>. Applied inside <see cref="CombatRaid.ComputePlan"/>
        /// for the duration of one synchronous <c>TryStartPotential</c> call, then reset to 1.
        /// Non-scheduled raids always see 1.
        /// </summary>
        public float NextScheduledRaidStrengthMul = 1f;
        /// <summary>
        /// Transient (not saved): set by <see cref="CombatRaid.ComputePlan"/> when it actually
        /// folded a non-1 <see cref="NextScheduledRaidStrengthMul"/> into a plan. Lets
        /// <see cref="RaidStoryAspect"/> tell a raid that landed apart from one whose event
        /// only spawned a carrier (breach capsule, stasis pod) and will compute its plan
        /// later: the pending storm is cleared only in the first case.
        /// </summary>
        public bool ScheduledRaidStrengthMulConsumed;
        /// <summary>
        /// Set by the generator's <see cref="SpacingStoryAspect"/>, null when it has none
        /// (Chaos, None, Tutorial).
        /// </summary>
        public SpacingStoryAspect Spacing;
        /// <summary>
        /// True while a recent event holds the flexible event sources back, see
        /// <see cref="SpacingStoryAspect"/>.
        /// </summary>
        public bool IsQuiet => Spacing != null && Spacing.IsQuiet(this);
        public void PrioritizeEvent(StoryEventDef ev, string why) {
            if (ModifierDisableStoryEvents.IsDisabledFor(S, ev)) {
                D.Warn("Refusing to prioritize a story event that is disabled via modifiers: {0}",
                    ev.Id);
                return;
            }
            if (PrioritizedEvents.TryGetValue(ev.IdH, out var tick))  {
                D.Err("Event already prioritized: {0} {1} ago | {2}",
                    ev, G.Ticks - tick, why);
                return;
            }
            PrioritizedEvents[ev.IdH] = G.Ticks;
            // D.Warn("Prioritizing story event: {0} ({1})", ev.Id, why);
        }

        public StorySchedule ScheduleFor(StoryEventOutcome outcome) {
            return Schedule[(int) outcome];
        }
        public StorySchedule ScheduleFor(StoryEventDef def) {
            return Schedule[(int) def.Outcome];
        }
        public StorySchedule ScheduleFor(StoryEventData data) {
            return Schedule[(int) data.Def.Outcome];
        }

        public void StartEvent(StoryEventData data) {
            if (data == null) {
                D.Err("Trying to start a null story event!");
                return;
            }
            if (ModifierDisableStoryEvents.IsDisabledFor(S, data.Def)) {
                D.Warn("Refusing to start a story event that is disabled via modifiers: {0}",
                    data.Id);
                return;
            }
            data.Location = CurrentSector;
            data.StartTick = Ticks;
            data.SetState(S, StoryEventState.Starting);
            // Store the amount actually reserved (after clamping) for accurate refund in Finalize
            data.PointsReserved = -ChangePoints(data.Def.Outcome, -data.Def.Points,
                data.Def, "Reserving");
            LogStart(data);
            if (PrioritizedEvents.Remove(data.Def.IdH)) {
            #if DEBUG_EVENTS
                D.Warn("STARTING EVENT (PRIORITIZED): {0}", data);
            } else {
                D.Warn("STARTING EVENT: {0}", data);
            #endif
            }
            try {
                data.Def.Event.OnStart(this, data);
                OngoingEvents.Add(data);
                if (StoryEventData.IsOK(data)) {
                    Spacing?.OnEventStarted(this, data);
                }
            } catch (Exception ex) {
                var msg = string.Format("Failed starting story event due to error: {0}", data);
                D.LogEx(ex, msg);
                data.Def.IsDisabled = true;
                var evtType = data.Def.Event.GetType();
                if (ModRegistry.IsModType(evtType, out var mod)) {
                    ModRegistry.MarkRuntimeBroken(mod, msg);
                    ModRegistry.ShowModErrorPopup(evtType, mod, msg);
                }
            }
        }

        // OnDemand defs never go through the picker, so whatever starts one on its own (a
        // curated beat, StructureCollapseSys) makes the checks the picker would have made.
        // Not started through the outcome's picker, so it puts no cooldown on the schedule
        public bool TryStartOnDemand(StoryEventDef def, string why) {
            if (def.IsDisabled) {
                return false;
            }
            if (def.Require != null
                    && !S.Situation.Matches(def.Require, S.Difficulty.Level)) {
                return false;
            }
            if (IsCoolingDown(def) || !def.Event.CheckConditions(this, def)) {
                return false;
            }
            var data = StoryEventData.CreateFor(def);
            data.Seed = Rng.Int;
            D.Warn("Starting on demand event ({0}): {1}->{2}",
                why, Units.TicksToTime(Ticks), def);
            StartEvent(data);
            return StoryEventData.IsOK(data);
        }

        public void OnTick(long ticks) {
            Ticks = ticks;
            IsEvery15Min = ticks % Consts.TicksPer15Minutes == 3;
            IsEveryHour = ticks % Consts.TicksPerHour == 7;
            IsEveryDay = ticks % Consts.TicksPerDay == 13;
        }

        public void OnLoad() {
            CurrentSector = SOUtils.GetPlayerSector(S);
            foreach (var data in OngoingEvents) {
                data.Def.Event.OnLoad(this, data);
            }
        }

        public bool CanSchedule(StoryEventOutcome outcome) {
            return Schedule[(int) outcome].CanScheduleNow(Ticks);
        }
        public EventSeenData SeenDataFor(StoryEventDef def) {
            if (Seen.TryGetValue(def.IdH, out var data)) {
                return data;
            }
            return default;
        }
        public bool IsCoolingDown(StoryEventFlags flag) {
            if (FlagCooldowns.TryGetValue(flag, out var ticks)) {
                if (ticks > Ticks) {
                    #if DEBUG_COOLDOWN
                    D.Warn("Flag is cooling down: {0}. {1}",
                        flag, Units.TicksToTime(ticks - Ticks));
                    #endif
                    return true;
                }
            }
            return false;
        }

        public bool TryGetCooldown(StoryEventFlags flag, out bool isCoolingDown) {
            if (FlagCooldowns.TryGetValue(flag, out var ticks)) {
                isCoolingDown = ticks > Ticks;
                return true;
            }
            isCoolingDown = false;
            return false;
        }
        public void CoolDownFlag(StoryEventFlags flag, int ticksFromNow) {
            #if DEBUG_COOLDOWN
            D.Warn("Setting cooldown: {0} -> {1}", flag,
                Units.TicksToTime(ticksFromNow));
            #endif

            // Check if flag is not compound (i.e., only a single bit is set)
            if ((flag & (flag - 1)) != 0) {
                D.Err("CoolDownFlag called with compound flag: {0}", flag);
                return;
            }

            FlagCooldowns[flag] = Ticks + ticksFromNow;
        }

        public bool AutoCoolDownFlag(StoryEventFlags flag,
                int autoExtendTicksWindow = 0) {
            var def = StoryEventFlagCooldownDef.Get(flag);
            if (def == null) {
                D.Err("Trying to cooldown a flag that has no cooldown def: {0}", flag);
                return false;
            }
            if (def.DoNotExtend || autoExtendTicksWindow > 0) {
                if (FlagCooldowns.TryGetValue(flag, out var cooldown)) {
                    if (cooldown - autoExtendTicksWindow > G.Ticks) {
                        // won't extend
                        return false;
                    }
                }
            }
            var cooldownTicks = def.GetCoolDownTicks(S.Difficulty.Level, Rng);
            if (cooldownTicks > 0) {
                #if DEBUG_COOLDOWN
                D.Warn("Auto cooling flag: {0}->{1}", flag,
                    Units.TicksToTime(cooldownTicks));
                #endif
                CoolDownFlag(flag, cooldownTicks);
                return true;
            } else {
                D.Err("Got 0 cooldown ticks for: {0} (diff: {1})", def, S.Difficulty.Id);
            }
            return false;
        }

        public bool IsCoolingDown(StoryEventDef def) {
            foreach (var fc in FlagCooldowns) {
                var flag = fc.Key;
                if ((def.Flags & flag) == flag) {
                    var ticks = fc.Value;
                    if (ticks > Ticks) {
                        #if DEBUG_COOLDOWN
                        D.Warn("Flag is cooling down: {0} for {1}. Won't start {2}",
                            flag, Units.TicksToTime(ticks - Ticks), def);
                        #endif
                        return true;
                    }
                }
            }
            return false;
        }

        public void Initialize(StoryGenDef gen, bool isLoading) {
            Generator = gen;
            Ticks = G.Ticks;
            StorySchedule.Count = Enum.GetValues(typeof(StoryEventOutcome)).Length;
            if (Schedule == null) {
                // D.Warn("Creating schedule");
                Schedule = new StorySchedule[StorySchedule.Count];
                for (int i = 0; i < StorySchedule.Count; i++) {
                    var sch = new StorySchedule();
                    sch.Outcome = (StoryEventOutcome) i;
                    Schedule[i] = sch;
                }
            // } else {
                // D.Warn("Schedule already created");
            }

            foreach (var sch in Schedule) {
                sch.DifficultyMod = GetDifficultyModFor(sch.Outcome);
            }
        }

        private StoryScheduleModifier GetDifficultyModFor(StoryEventOutcome outcome) {
            if (S.Difficulty.StoryScheduleModifiers == null) {
                D.Err("StoryScheduleModifiers is null for difficulty: {0}", S.Difficulty.Id);
                return StoryScheduleModifier.Default;
            }
            var outcomeS = outcome.ToString();
            foreach (var mod in S.Difficulty.StoryScheduleModifiers) {
                if (mod.Schedule == outcomeS) {
                    return mod;
                }
            }
            D.Err("StoryScheduleModifiers for difficulty {0} does not contain schedule: {1}",
                S.Difficulty.Id, outcome);
            return StoryScheduleModifier.Default;
        }

        public void LogSeen(StoryEventData data) {
            if (data.State == StoryEventState.Failed) {
                D.Err("Trying to log failed event as seen! {0}", data);
                return;
            }
            if ((data.Flags & StoryEventDataFlags.SkipSeen)
                    == StoryEventDataFlags.SkipSeen) {
                return;
            }
            var def = data.Def;
            // check if there are curated event enqueued for this def
            if (PrioritizedEvents.Remove(def.IdH)) {
                D.Warn("Removed prioritized event since it was just seen: {0}",
                    def.Id);
            }
            if (!Seen.TryGetValue(def.IdH, out var seen)) {
                seen.Id = def.IdH;
            }
            seen.SeenCount++;
            seen.LastSeen = Ticks;
            Seen[def.IdH] = seen;
        }

        private void LogStart(StoryEventData data) {
            var def = data.Def;
            if (!Seen.TryGetValue(def.IdH, out var seen)) {
                seen.Id = def.IdH;
            }
            seen.StartCount++;
            seen.LastStart = Ticks;
            Seen[def.IdH] = seen;
        }

        public bool CanSchedulePeriodic(int outcomeIdx) {
            if (Ticks % Consts.TicksPer15Minutes != outcomeIdx) {
                return false;
            }
            if (IsQuiet) {
                return false;
            }
            var sch = Schedule[outcomeIdx];
            return sch.CanScheduleNow(Ticks);
        }

        /// <summary>
        /// Returns the actual change applied (may be less than |points| if clamped to 0).
        /// </summary>
        public int ChangePoints(StoryEventOutcome outcome, int points,
                StoryEventDef def, string why) {
            var sch = Schedule[(int) outcome];
            var pointsBefore = sch.PointAllowance;
            sch.PointAllowance += points;
            if (sch.PointAllowance < 0) {
                sch.PointAllowance = 0;
            }
            if (sch.MaxPointAllowance < sch.PointAllowance) {
                sch.MaxPointAllowance = sch.PointAllowance;
            }
            #if DEBUG_POINTS
            D.Warn("Changing points for schedule: {0}. {1} -> {2}/{3} ({4} | {5})",
                outcome, points, sch.PointAllowance, sch.MaxPointAllowance, why, def);
            #endif
            S.Sig.StoryScheduleAllowanceChanged.Send(sch,
                pointsBefore, sch.PointAllowance);
            return sch.PointAllowance - pointsBefore;
        }
        public void CooldownEvent(StoryEventData data) {
            #if DEBUG_COOLDOWN
            D.Warn("Cooling down: {0} | {1}", data, data.Flags);
            #endif
            StoryEventFlags flags;
            flags = data.EventFlagsActual;
            if (flags == StoryEventFlags.None) {
                if (!data.Def.Flags.HasFlag(StoryEventFlags.CooldownOnStart)) {
                    D.Err("StoryEventData has not been finalized and has no EventFlagsActual! Falling back to Def.Flags: {0}->{1}",
                        data, data.Def.Flags);
                }
                flags = data.Def.Flags;
            }
            for (int i = StoryEventDef.CoolableFlags.Length - 1; i >= 0; i--) {
                StoryEventFlags flag = StoryEventDef.CoolableFlags[i];
                bool contains = (flags & flag) == flag;
                if (contains) {
                    AutoCoolDownFlag(flag);
                }
            }
            data.Flags &= ~StoryEventDataFlags.NeedsCooldown;
        }

        public void Unload() {
            FlagCooldowns.Clear();
            foreach (var sch in Schedule) {
                sch.Reset();
            }
        }

        public void ClearRecentSeen(StoryEventDef def, long ticks) {
            if (!Seen.TryGetValue(def.IdH, out var seen)) { return; }
            if (seen.LastSeen == 0) { return; }
            if (def.MinRepeatDelayDays == 0) { return; }
            int minDelay = (int) (def.MinRepeatDelayDays * Consts.TicksPerDay);
            if (ticks - seen.LastSeen > minDelay) {
                D.Warn("Clearing recent seen for {0}", def.Id);
                seen.LastSeen = ticks - minDelay;
                Seen[def.IdH] = seen;
            }
        }

        public void ClearCooldown(StoryEventFlags flag) {
            // Check if flag is not compound (i.e., only a single bit is set)
            if ((flag & (flag - 1)) != 0) {
                D.Err("ClearCooldown called with compound flag: {0}", flag);
                return;
            }
            FlagCooldowns[flag] = G.Ticks - 1;
        }
        public bool IsEventRunning(int eventId, float minDifficulty = 0f) {
            if (OngoingEvents.Count == 0) { return false; }
            if (minDifficulty > 0f) {
                if (S.Difficulty.Level > minDifficulty) { return false; }
            }
            for (int i1 = OngoingEvents.Count - 1; i1 >= 0; i1--) {
                StoryEventData ev = OngoingEvents[i1];
                if (ev.Def.IdH == eventId) {
                    return true;
                }
            }
            return false;
        }
        public bool IsAnyEventRunning(int evId1, int evId2, float minDifficulty = 0f) {
            if (OngoingEvents.Count == 0) { return false; }
            if (minDifficulty > 0f) {
                if (S.Difficulty.Level > minDifficulty) { return false; }
            }
            for (int i1 = OngoingEvents.Count - 1; i1 >= 0; i1--) {
                StoryEventData ev = OngoingEvents[i1];
                var id = ev.Def.IdH;
                if (id == evId1 || id == evId2) {
                    return true;
                }
            }
            return false;
        }

        public bool HasSectorEffect(int effectIdH, float minDifficulty = 0f) {
            if (minDifficulty > 0f) {
                if (S.Difficulty.Level > minDifficulty) {
                    return false;
                }
            }
            return CurrentSector.Biome.HasEffect(effectIdH);
        }
        public bool HasAnySectorEffect(int effectIdH, int effectIdH2, float minDifficulty = 0f) {
            if (minDifficulty > 0f) {
                if (S.Difficulty.Level > minDifficulty) {
                    return false;
                }
            }
            foreach (var eff in CurrentSector.Biome.Effects) {
                var id = eff.Key.IdH;
                if (id == effectIdH || id == effectIdH2) {
                    return true;
                }
            }
            return false;
        }
    }
}
