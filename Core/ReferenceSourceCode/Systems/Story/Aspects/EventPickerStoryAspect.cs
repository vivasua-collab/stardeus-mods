#if !PRODUCTION_BUILD
// #define DEBUG_TIME_BUDGET
// #define PROFILE_CONSIDER
// #define DEBUG_CONDITIONS
// #define DEBUG_WEIGHTS
// #define DEBUG_COOLDOWNS
// #define VERBOSE_IGNORE
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class EventPickerStoryAspect : StoryAspect, IStoryEventHandler,
            ISaveableChild {

        public enum IgnoreType {
            Points,
            Cooldowns,
            Repetition,
            SectorDifficulty,
        }
        public static readonly IgnoreType[] ignoreTypes =
            (IgnoreType[]) Enum.GetValues(typeof(IgnoreType));
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new EventPickerStoryAspect();
        }

        private SituationCheckLevel situationCheck;
        public const string AspectId = "EventPicker";
        public override string Id => AspectId;
        private const float considerTimeBudget = 0.001f;
        private const StoryEventFlags CanSkipRepeatDelay =
            StoryEventFlags.CanIgnoreRepeatDelay | StoryEventFlags.Fallback;
        private StoryEventOutcome? outcome;
        private int outcomeIdx;
        private bool isManual;
        public StoryEventOutcome HandlesOutcome => outcome ??= ParseOutcome();
        private Curve countWeightCurve;
        private Curve seenDaysAgoWeightCurve;
        private Curve startDaysAgoWeightCurve;
        private Curve scheduleCooldownHoursCurve;
        private Curve maxIgnoresCurve;
        private const float tickTimeBudget = 0.001f;
        private readonly Dictionary<IgnoreType, bool> ignores = new();
        private readonly Dictionary<IgnoreType, float> ignoreChances = new();
        private int maxIgnores;
        private int ignoreChangeDelayTicks;
        private long nextIgnoreChange;

        public override void OnTick(StoryContext ctx) {
            UpdateIgnores(ctx);
            if (isManual) { return; }
            var toConsider = ctx.EventsToConsider;
            var potential = ctx.PotentialEvents;
            var toConsiderCnt = toConsider.Count;
            var potentialCnt = potential.Count;
            if (toConsiderCnt == 0) {
                if (potentialCnt == 0) {
                    if (ctx.CanSchedulePeriodic(outcomeIdx)) {
                        // start considering potential events
                        foreach (var ev in StoryEventDef.Considerable[outcomeIdx]) {
                            toConsider.Enqueue(ev);
                        }
                    }
                } else {
                    // A hook may have opened a quiet period since this pick was queued
                    if (ctx.IsQuiet) {
                        potential.Clear();
                        return;
                    }
                    var started = TryStartPotential(ctx, potential, tickTimeBudget);
                    if (started != null) {
                        // A periodic start; counts against the started event's own window.
                        // The queue is shared, so the picker running the start is usually not
                        // the one that queued it
                        ctx.ScheduleFor(started.Def).Executed++;
                    }
                    return;
                }
            }

            ConsiderEvents(ctx, toConsider, potential);
        }

        private void UpdateIgnores(StoryContext ctx) {
            if (maxIgnores == 0) { return; }
            if (nextIgnoreChange > ctx.Ticks) { return; }
            var rng = ctx.Rng.Fork();

            var numIgnores = rng.IntCurve(maxIgnoresCurve);
            // D.Warn("New ignore cycle. Num ignores: {0} ({1})", numIgnores, Def.Id);
            if (numIgnores == 0) {
                for (int i = ignoreTypes.Length - 1; i >= 0; i--) {
                    var ignType = ignoreTypes[i];
                    ignores[ignType] = false;
                }
                // A no-ignore cycle lasts as long as any other: returning before this re-rolled
                // on the next tick, so an ignore was active nearly all the time
                nextIgnoreChange = ctx.Ticks + ignoreChangeDelayTicks;
                return;
            }

            Arrays.Shuffle(rng, ignoreTypes);

            for (int i = ignoreTypes.Length - 1; i >= 0; i--) {
                var ignType = ignoreTypes[i];
                bool ignore = false;
                if (numIgnores > 0) {
                    ignore = rng.Chance(ignoreChances[ignType]);
                    if (ignore) {
                        // D.Warn("Choosing to ignore: {0} ({1})", ignType, Def.Id);
                        numIgnores--;
                    }
                }
                ignores[ignType] = ignore;
            }
            nextIgnoreChange = ctx.Ticks + ignoreChangeDelayTicks;
        }

        public StoryEventData TryStartPotential(StoryContext ctx,
                Dictionary<StoryEventDef, float> potential, float timeBudget) {
            var data = TryPreparePotential(ctx, potential, timeBudget);
            if (data != null) {
                if (StartEvent(data, ctx)) {
                    ctx.EventsToConsider.Clear();
                    return data;
                }
                if (!data.Flags.HasFlag(StoryEventDataFlags.SoftFailure)) {
                    D.Err("Failed to start event: {0}", data);
                }
            }
            return null;
        }

        public StoryEventData TryPreparePotential(StoryContext ctx,
                Dictionary<StoryEventDef, float> potential, float timeBudget) {
            // Pick and execute one of the potential events
            double startTime = Time.realtimeSinceStartupAsDouble;
            double timeLimit;
            if (timeBudget > 0d) {
                timeLimit = startTime + timeBudget;
            } else {
                timeLimit = 0d;
            }
            int processed = 0;
            while (potential.Count > 0) {
                if (ctx.Rng.TryWeightedFrom(potential, out var def)) {
                    potential.Remove(def);
                    processed++;
                    if (def.Event.CheckConditions(ctx, def)) {
                        var data = StoryEventData.CreateFor(def);
                        data.Seed = ctx.Rng.Int;
                        data.SetState(ctx.S, StoryEventState.Starting);
                        potential.Clear();
                        return data;
                    }
                    if (timeLimit > 0d) {
                        var now = Time.realtimeSinceStartupAsDouble;
                        if (now >= timeLimit) {
                            #if DEBUG_TIME_BUDGET
                            D.Warn("Ran out of time limit while trying to start potential events. Processed: {0}. Left: {1}. Time Budget: {2:0.00000}s",
                                processed, potential.Count, timeBudget);
                            #endif
                            return null;
                        }
                    }
                }
            }
            D.Warn("Failed to start all potential events");
            return null;
        }

        private void ConsiderEvents(StoryContext ctx,
                Queue<StoryEventDef> toConsider,
                Dictionary<StoryEventDef, float> potential) {
            double startTime = Time.realtimeSinceStartupAsDouble;
            int considered = 0;
            #if PROFILE_CONSIDER
            double lastStartTime = startTime;
            #endif
            while (toConsider.TryDequeue(out var def)) {
                var w = GetEventWeight(ctx, def, StoryPickIgnore.None);
                if (w > 0f) {
                    D.Log("Adding potential event: {0}->{1}", def.Id, w);
                    potential.Add(def, w);
                }
                considered++;
                var time = Time.realtimeSinceStartupAsDouble;
                #if PROFILE_CONSIDER
                var considerTime = time - lastStartTime;
                if (considerTime > 0.00005f) {
                    D.Log("Considered {0} for {1:0.00000}s ({2})",
                        def.Id, considerTime, Def);
                }
                lastStartTime = time;
                #endif
                if (time - startTime > considerTimeBudget) {
                    #if !PRODUCTION_BUILD
                    // D.Warn("Exceeded consider event budget after considering {0} events ({1})",
                    //     considered, Def);
                    #endif
                    break;
                }
            }
            #if PROFILE_CONSIDER
            D.Log("Considered {0} events in {1:0.00000}s ({2})", considered,
                Time.realtimeSinceStartupAsDouble - startTime, Def);
            #endif
        }

        private void CooldownSchedule(StorySchedule sch, StoryContext ctx, StoryEventData data) {
            var ticks = (int) (ctx.Rng.Curve(scheduleCooldownHoursCurve) * Consts.TicksPerHour);
            #if DEBUG_COOLDOWNS
            D.Warn("Cooling down schedule {0} for {1} after starting event {2}",
                sch.Outcome, Units.TicksToTime(ticks), data);
            #endif
            sch.CooldownUntil = ctx.Ticks + ticks;
        }

        public float GetEventWeight(StoryContext ctx, StoryEventDef def,
                StoryPickIgnore ignore) {
            if ((ignore & StoryPickIgnore.Schedule) != StoryPickIgnore.Schedule
                        && !ctx.CanSchedule(def.Outcome)) {
                if (def.RunASAPWhenPrioritized &&
                        ctx.PrioritizedEvents.ContainsKey(def.IdH)) {
                    // allowing prioritized event off schedule
                } else {
                    #if DEBUG_WEIGHTS
                    D.Warn("Skipping event {0} due to inability to schedule. Ignore: {1}",
                        def, ignore);
                    #endif
                    return 0f;
                }
            }
            var seen = ctx.SeenDataFor(def);
            if (!CanStartNow(seen, def, ctx, ignore)) {
                #if DEBUG_WEIGHTS
                D.Warn("Skipping event {0} due to inability to start now. Ignore: {1}. Seen: {2}",
                    def, ignore, seen);
                #endif
                return 0f;
            }
            return WeightFor(def, ctx, seen);
        }

        private float WeightFor(StoryEventDef def, StoryContext ctx,
                EventSeenData seen) {

            if (ctx.PrioritizedEvents.TryGetValue(def.IdH, out var since)) {
                return Mathf.Min(1000f + since * 10f, 10000000f);
            }

            var seenCountW = countWeightCurve.Evaluate(seen.SeenCount);
            var lastSeen = seen.LastSeen;
            float seenDaysAgoW;
            float seenDaysAgo;
            if (lastSeen == 0L) {
                // never seen, so let's default this to
                seenDaysAgoW = 1f;
                seenDaysAgo = -1f;
            } else {
                var ticksSinceSeen = ctx.Ticks - seen.LastSeen;
                // float division: integer days sampled the curves only at whole days
                seenDaysAgo = ticksSinceSeen / (float) Consts.TicksPerDay;
                seenDaysAgoW = seenDaysAgoWeightCurve.Evaluate(seenDaysAgo);
            }

            var lastStart = seen.LastStart;
            float startDaysAgo;
            float startDaysAgoW;
            if (lastStart == 0L) {
                startDaysAgo = -1f;
                startDaysAgoW = 1f;
            } else {
                var ticksSinceStart = ctx.Ticks - seen.LastStart;
                startDaysAgo = ticksSinceStart / (float) Consts.TicksPerDay;
                startDaysAgoW = startDaysAgoWeightCurve.Evaluate(startDaysAgo);
            }

            var w = seenCountW * seenDaysAgoW * startDaysAgoW * def.Rarity;
            // Growth events get rarer against their competitors as the colony fills up.
            // Relative, not absolute: the stasis wake-up interval is the absolute pacing
            // path, and it stamps the ColonyGrowthBio cooldown so joiners cannot outrun it.
            // CanStartNow already returned false for intent 0, so w stays positive here.
            if (def.IsColonyGrowthBio) {
                w *= ColonyGrowth.BioIntent(ctx);
            } else if (def.IsColonyGrowthRobo) {
                w *= ColonyGrowth.Intent(ctx);
            }
            // Costly events will have lower weight
            w += (StoryEventDef.MaxPoints + 1 - def.Points) * 0.0001f;
            #if DEBUG_WEIGHTS
            D.Warn(string.Format(
                "Event w: {0}->{1}. Seen: {2}x. Ago: {3:0.#}d. Start: {4}x. Ago: {5:0.#}d. ({6}*{7}*{8}*{9}) ({10})",
                def.Id, w, seen.SeenCount, seenDaysAgo, seen.StartCount, startDaysAgo,
                seenCountW, seenDaysAgoW, startDaysAgoW,
                def.Rarity, Def));
            #endif
            if (w <= 0f) {
                D.Err("Weight <= 0f for event! Fixing to 0.001f {0}", def);
                w = 0.001f;
            }
            return w;
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            outcomeIdx = (int) HandlesOutcome;
            isManual = Def.Vars.Get(PropertyIdH.Manual, false);

            // for random / chaos generators
            maxIgnores = Def.Vars.Get(PropertyIdH.MaxIgnores, 0);
            if (maxIgnores > 0) {
                ignoreChances[IgnoreType.SectorDifficulty] = Def.Vars.Get(PropertyIdH.IgnoreSectorDifficulty, 0f);
                ignoreChances[IgnoreType.Repetition] = Def.Vars.Get(PropertyIdH.IgnoreRepetition, 0f);
                ignoreChances[IgnoreType.Cooldowns] = Def.Vars.Get(PropertyIdH.IgnoreCooldowns, 0f);
                ignoreChances[IgnoreType.Points] = Def.Vars.Get(PropertyIdH.IgnorePoints, 0f);
                ignoreChangeDelayTicks = Def.Vars.Get(PropertyIdH.IgnoreChangeHours, 72);
                ignoreChangeDelayTicks *= Consts.TicksPerHour;
                LoadCurve(PropertyIdH.MaxIgnoresCurve, ref maxIgnoresCurve);
            }

            var sitCheck = Def.Vars.Get(PropertyIdH.SituationCheck, "Full");
            if (!Enums.TryFromString(sitCheck, out situationCheck)) {
                D.Err("SituationCheck incorrectly defined in {0}: {1}", Id, sitCheck);
                situationCheck = SituationCheckLevel.Full;
            }
            LoadCurve(PropertyIdH.SeenCountWeightCurve, ref countWeightCurve);
            LoadCurve(PropertyIdH.StartDaysAgoWeightCurve, ref startDaysAgoWeightCurve);
            LoadCurve(PropertyIdH.SeenDaysAgoWeightCurve, ref seenDaysAgoWeightCurve);
            LoadCurve(PropertyIdH.ScheduleCooldownHoursCurve, ref scheduleCooldownHoursCurve);
        }

        public override void Unload(StoryContext ctx) {
        }

        /// <summary>
        /// Checks if event was seen if once per game flag is set, and
        /// checks last seen time VS min repeat delay days.
        /// </summary>
        public bool CanStartNow(EventSeenData seen, StoryEventDef def,
                StoryContext ctx, StoryPickIgnore ignore) {
            if (def.IsDisabled) { return false; }
            // Every pick path comes through here (windows, hooks, curated force_start), so
            // Peaceful's "no major threats" holds everywhere with this one check
            if (!ctx.S.Difficulty.AllowMajorThreats && def.IsMajorThreat) {
                #if DEBUG_CONDITIONS
                D.Warn("Major threats are off on this difficulty: {0}", def.Id);
                #endif
                return false;
            }
            // Population intent is the only opinion the story system has about growth: 0 at
            // the ceiling closes every path, and the hold keeps a biological joiner off the
            // heels of a wake-up or a death. Between 0 and 1 the throttle is the weight
            // multiplier in WeightFor.
            if (def.IsColonyGrowthBio) {
                if (ColonyGrowth.BioIntent(ctx) <= 0f || ColonyGrowth.IsHolding(ctx)) {
                    #if DEBUG_CONDITIONS
                    D.Warn("Won't allow colony growth event: {0}", def.Id);
                    #endif
                    return false;
                }
            } else if (def.IsColonyGrowthRobo) {
                if (ColonyGrowth.Intent(ctx) <= 0f) {
                    #if DEBUG_CONDITIONS
                    D.Warn("Won't allow colony growth event: {0}", def.Id);
                    #endif
                    return false;
                }
            }

            if ((def.Flags & StoryEventFlags.NoHyperspace)
                    == StoryEventFlags.NoHyperspace && A.IsInHyperspace) {
                #if DEBUG_CONDITIONS
                D.Warn("Can't start event due to hyperspace: {0}", def);
                #endif
                return false;
            }
            var ignoreThis = ignore;
            bool runASAP = false;
            // allow prioritized social events to happen off-schedule
            if (def.RunASAPWhenPrioritized && ctx.PrioritizedEvents.ContainsKey(def.IdH)) {
                runASAP = true;
                ignoreThis |= StoryPickIgnore.Schedule | StoryPickIgnore.RepeatDelay;
            }
            if (!CheckPoints(ctx, def, ignoreThis)) {
                if (def.Points == 0) {
                    D.Err("Def points is 0 but CheckPoints failed! {0}", def);
                }
                #if DEBUG_CONDITIONS
                D.Warn("Can't start event due to not enough StoryPoints: {0}", def);
                #endif
                return false;
            }
            if (!CheckOngoingEvents(ctx, def)) {
                #if DEBUG_CONDITIONS
                D.Warn("Can't start event due to ongoing: {0} | {1}",
                    def, D.DumpCollection(ctx.OngoingEvents));
                #endif
                return false;
            }
            if (!runASAP && !CheckRepetition(ctx, def, seen, ignoreThis)) {
                #if DEBUG_CONDITIONS
                D.Warn("Can't start event due to repetition: {0} | {1}", def, seen);
                #endif
                return false;
            }

            if (!CheckRequirements(ctx, def)) {
                #if DEBUG_CONDITIONS
                D.Warn("Can't start event due to requirements check: {0}", def);
                #endif
                return false;
            }
            if (!runASAP && ctx.IsCoolingDown(def)) {
                if (!CheckIgnoreChance(ctx, def, IgnoreType.Cooldowns)) {
                    #if DEBUG_CONDITIONS
                    D.Warn("Can't start event due to cooling down: {0}", def);
                    #endif
                    return false;
                }
            }
            if ((ignoreThis & StoryPickIgnore.SectorDifficulty) != StoryPickIgnore.SectorDifficulty
                    && !MatchesSectorDifficulty(def, ctx)) {
                #if DEBUG_CONDITIONS
                D.Warn("Can't start event due to sector difficulty mismatch: {0}", def);
                #endif
                return false;
            }

            return def.Event.CheckConditions(ctx, def);
        }

        private bool CheckPoints(StoryContext ctx, StoryEventDef def, StoryPickIgnore ignore) {
            if ((ignore & StoryPickIgnore.Points) == StoryPickIgnore.Points) {
                return true;
            }
            if (CheckIgnoreChance(ctx, def, IgnoreType.Points)) {
                return true;
            }
            return def.CheckPoints(ctx, ignore);
        }

        private bool CheckRequirements(StoryContext ctx, StoryEventDef def) {
            if (def.Require == null) { return true; }
            switch (situationCheck) {
                case SituationCheckLevel.Skip: return true;
                case SituationCheckLevel.Partial: {
                    if (!ctx.S.Situation.MatchesLight(def.Require, ctx.S.Difficulty.Level)) {
                        #if DEBUG_CONDITIONS
                        D.Warn("Situation (PARTIAL) doesn't match for event: {0} ({1})", def, Def);
                        #endif
                        return false;
                    }
                    return true;
                }
                case SituationCheckLevel.Full: {
                    if (!ctx.S.Situation.Matches(def.Require, ctx.S.Difficulty.Level)) {
                        #if DEBUG_CONDITIONS
                        D.Warn("Situation doesn't match for event: {0} ({1})", def, Def);
                        #endif
                        return false;
                    }
                    return true;
                }
                default: {
                    D.Err("Unhandled situation check level: {0}", situationCheck);
                    return true;
                }
            }
        }

        private bool CheckRepetition(StoryContext ctx, StoryEventDef def,
                EventSeenData seen, StoryPickIgnore ignore) {
            if (seen.SeenCount == 0) { return true; }
            if ((def.Flags & StoryEventFlags.OncePerGame)
                    == StoryEventFlags.OncePerGame) {
                return false;
            }
            if (def.MinRepeatDelayDays <= 0f) { return true; }
            var checkDelay = (ignore & StoryPickIgnore.RepeatDelay) != StoryPickIgnore.RepeatDelay;
            // only events with CanIgnoreRepeatDelay or Fallback flags can skip
            // this check
            if (!checkDelay && ((def.Flags & CanSkipRepeatDelay) == StoryEventFlags.None)) {
                checkDelay = true;
            }
            if (checkDelay) {
                var ticksSinceSeen = ctx.Ticks - seen.LastSeen;
                if (def.MinRepeatDelayDays * Consts.TicksPerDay > ticksSinceSeen) {
                    return CheckIgnoreChance(ctx, def, IgnoreType.Repetition);
                }
            }
            return true;
        }

        private bool CheckOngoingEvents(StoryContext ctx, StoryEventDef def) {
            if (ctx.OngoingEvents.Count <= 0) { return true; }
            if ((def.Flags & StoryEventFlags.CanHaveDuplicates)
                    == StoryEventFlags.CanHaveDuplicates) {
                return true;
            }
            for (int i = ctx.OngoingEvents.Count - 1; i >= 0; i--) {
                StoryEventData ev = ctx.OngoingEvents[i];
                if (ev.Def == def) {
                    return false;
                }
            }
            return true;
        }

        public bool StartEvent(StoryEventData data, StoryContext ctx) {
            if (data == null) {
                D.Err("Trying to start a story event with null data");
                return false;
            }
            ctx.StartEvent(data);
            var started = StoryEventData.IsOK(data);
            if (started) {
                var sch = ctx.ScheduleFor(data.Def);
                CooldownSchedule(sch, ctx, data);
            }
            PlayStats.TrackStoryEventStart(data.Def.Id, started);
            return started;
        }

        public StoryEventData TriggerEvent(StoryEventDef def, StoryContext ctx) {
            var data = StoryEventData.CreateFor(def);
            data.Seed = ctx.Rng.Int;
            StartEvent(data, ctx);
            return data;
        }
        private bool MatchesSectorDifficulty(StoryEventDef def, StoryContext ctx) {
            var hasSecDifficultyCheck = !Arrays.IsEmpty(def.SectorDifficultyChance);
            if (!hasSecDifficultyCheck) {
                // Ensure space events have this check!
                if ((def.Flags & StoryEventFlags.Space) == StoryEventFlags.Space) {
                    D.Err("SpaceEvent has no SectorDifficultyChance! {0}", def);
                }
                return true;
            }
            if (ctx.CurrentSector == null) {
                D.Err("StoryContext has null CurrentSector!");
                return false;
            }
            if (ctx.CurrentSector.Seed == 0) {
                D.Err("Seed is not set on SpaceSector! {0}", ctx.CurrentSector);
            }
            var matches = SectorRollAllows(def, ctx.CurrentSector.Seed,
                ctx.CurrentSector.Biome.Difficulty);
            if (!matches && CheckIgnoreChance(ctx, def, IgnoreType.SectorDifficulty)) {
                return true;
            }
            return matches;
        }

        // The seeded part of the sector check. Seeded by the sector, so a sector keeps its
        // answer across reloads and visits, and by the event, so each event has its own: one
        // roll for every event made a sector all or nothing, and a high one vetoed nearly
        // every space event at once. A plain Hashes.Quick seed into Rng left two events' rolls
        // correlated, the noise hash mixes them apart
        public static bool SectorRollAllows(StoryEventDef def, int sectorSeed,
                float sectorDifficulty) {
            if (Arrays.IsEmpty(def.SectorDifficultyChance)) {
                return true;
            }
            var chance = def.SectorDifficultyCurve.Evaluate(sectorDifficulty);
            if (chance <= Maths.Epsilon) {
                return false;
            }
            if (chance >= 1f) {
                return true;
            }
            return chance > Rng2.ZeroToOne.Noise2D(sectorSeed, def.IdH, (uint) Primes.Prime019);
        }

        /// <returns>true if we we ignore the thing</returns>
        private bool CheckIgnoreChance(StoryContext ctx,
                StoryEventDef def, IgnoreType type) {
            if (ignores.TryGetValue(type, out var ignore)) {
                return ignore;
            }
            return false;
        }

        private string DataKey(string key) {
            return $"{Def.Id}.{key}";
        }

        public void OnSave(ComponentData allAspectData) {
            if (maxIgnores > 0) {
                allAspectData.SetBool(DataKey("ign.cdown"), ignores[IgnoreType.Cooldowns]);
                allAspectData.SetBool(DataKey("ign.pts"), ignores[IgnoreType.Points]);
                allAspectData.SetBool(DataKey("ign.rep"), ignores[IgnoreType.Repetition]);
                allAspectData.SetBool(DataKey("ign.sdiff"), ignores[IgnoreType.SectorDifficulty]);
                allAspectData.SetLong(DataKey("ign.chng"), nextIgnoreChange);
            }
        }

        public void OnLoad(ComponentData allAspectData) {
            if (maxIgnores > 0) {
                ignores[IgnoreType.Cooldowns] = allAspectData.GetBool(DataKey("ign.cdown"), false);
                ignores[IgnoreType.Points] = allAspectData.GetBool(DataKey("ign.pts"), false);
                ignores[IgnoreType.Repetition] = allAspectData.GetBool(DataKey("ign.rep"), false);
                ignores[IgnoreType.SectorDifficulty] = allAspectData.GetBool(DataKey("ign.sdiff"), false);
                nextIgnoreChange = allAspectData.GetLong(DataKey("ign.chng"), 0L);
            }
        }
    }
}
