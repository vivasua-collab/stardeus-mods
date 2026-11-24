#if !PRODUCTION_BUILD
// #define DEBUG_CURATED
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class CuratedStoryAspect : StoryAspect {
        private const int MaxTickCollisionRetries = 100;
        private const string AfterPrefix = "after:";
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new CuratedStoryAspect();
        }

        public const string AspectId = "Curated";
        public override string Id => AspectId;

        private readonly Dictionary<long, (StoryEventDef, bool)> upcomingEvents = new();

        // A beat keyed to player progress ("after:<QuestBool>+<days>"): due that many days
        // after the quest bool is set, and at its day at the latest. The opening exists to
        // let the tutorial spine finish before things happen, and a fast player finishes it
        // in a third of the time a slow one needs, so a fixed day is dead air for one and a
        // rush for the other. Its state lives in AspectData, since unlike a fixed day it
        // cannot be recomputed on load.
        private sealed class MilestoneBeat {
            public StoryEventDef Def;
            public bool ForceStart;
            public string QuestBool;
            public long OffsetTicks;
            public long CeilingTick;
            public string DueKey;
            public string DoneKey;
        }
        private readonly List<MilestoneBeat> milestoneBeats = new();

        public override void OnTick(StoryContext ctx) {
            if (ctx.IsEveryHour && milestoneBeats.Count > 0) {
                TickMilestoneBeats(ctx);
            }
            if (!upcomingEvents.TryGetValue(ctx.Ticks, out var evt)) {
                return;
            }
            var def = evt.Item1;
            var forceStart = evt.Item2;

            if (IsSpentOncePerGame(ctx, def)) {
                return;
            }
            if (forceStart && TryForceStart(ctx, def)) {
                return;
            }
            if (forceStart) {
                D.Warn("Could not start curated event (force_start), will prioritize instead: {0}->{1}",
                    Units.TicksToTime(ctx.Ticks), def);
            }
            ctx.PrioritizeEvent(def,
                $"Curated story: {Units.TicksToTime(ctx.Ticks)}");
        }

        private static bool IsSpentOncePerGame(StoryContext ctx, StoryEventDef def) {
            if (!def.Flags.HasFlag(StoryEventFlags.OncePerGame)) {
                return false;
            }
            var seen = ctx.SeenDataFor(def);
            if (seen.SeenCount > 0) {
                D.Warn("Skipping curated event, once per game but already seen: {0}",
                    def, Units.TicksToTime(ctx.Ticks));
                return true;
            }
            return false;
        }

        private static bool TryForceStart(StoryContext ctx, StoryEventDef def) {
            var handler = ctx.S.Query.StoryEventHandlerFor.Ask(def.Outcome);
            var seen = ctx.SeenDataFor(def);
            // The sector roll is flavor for random picks. A scripted beat is a promise, and the
            // sector it is due in may be one the ship cannot leave yet
            if (!handler.CanStartNow(seen, def, ctx, StoryPickIgnore.Schedule
                    | StoryPickIgnore.Points | StoryPickIgnore.SectorDifficulty)) {
                return false;
            }
            var data = StoryEventData.CreateFor(def);
            data.Seed = ctx.Rng.Int;
            D.Warn("Starting curated event (force_start): {0}->{1}",
                Units.TicksToTime(ctx.Ticks), def);
            ctx.StartEvent(data);
            return true;
        }

        private void TickMilestoneBeats(StoryContext ctx) {
            var data = ctx.AspectData;
            var q = ctx.S.Query;
            for (int i = 0; i < milestoneBeats.Count; i++) {
                var beat = milestoneBeats[i];
                if (data.GetBool(beat.DoneKey, false)) {
                    continue;
                }
                var def = beat.Def;
                // A beat for something this game already had (a random capsule, a save from
                // before the beat existed) is spent, not repeated
                if (def.IsDisabled || ctx.SeenDataFor(def).StartCount > 0) {
                    data.SetBool(beat.DoneKey, true);
                    continue;
                }
                long due = data.GetInt(beat.DueKey, 0);
                if (due == 0 && q.GetQuestBool.Ask(beat.QuestBool)) {
                    due = ctx.Ticks + beat.OffsetTicks;
                    data.SetInt(beat.DueKey, (int) due);
                }
                var atCeiling = ctx.Ticks >= beat.CeilingTick;
                if (!atCeiling && (due == 0 || ctx.Ticks < due)) {
                    continue;
                }
                // Keeps state for a beat that may retry past its ceiling, so a load does not
                // take it for one from a save older than the beats and spend it
                if (due == 0) {
                    data.SetInt(beat.DueKey, (int) beat.CeilingTick);
                }
                if (TryMilestoneBeat(ctx, beat, atCeiling)) {
                    data.SetBool(beat.DoneKey, true);
                }
            }
        }

        // True when the beat is spent. A beat that cannot start yet (its Require, cooldown or
        // conditions) retries every hour. An OnDemand force_start beat is dropped at its
        // ceiling. Any other force_start beat keeps retrying past it and is never handed to the
        // picker: its outcome's window can stay shut for days, and the picker's gates can be
        // what blocks it, so the supply capsule was lost that way, and with it the first raid
        private static bool TryMilestoneBeat(StoryContext ctx, MilestoneBeat beat, bool atCeiling) {
            var def = beat.Def;
            if (IsSpentOncePerGame(ctx, def)) {
                return true;
            }
            // Without force_start an OnDemand beat is content for a hook (what a capsule holds)
            // and is only prioritized, below
            if (beat.ForceStart && def.Flags.HasFlag(StoryEventFlags.OnDemand)) {
                return ctx.TryStartOnDemand(def, "curated") || atCeiling;
            }
            if (beat.ForceStart) {
                if (TryForceStart(ctx, def)) {
                    return true;
                }
                if (atCeiling && ctx.Ticks - beat.CeilingTick < Consts.TicksPerHour) {
                    D.Warn("Curated beat could not start by its ceiling, will keep trying: {0}->{1}",
                        Units.TicksToTime(ctx.Ticks), def);
                }
                return false;
            }
            if (!ctx.PrioritizedEvents.ContainsKey(def.IdH)) {
                ctx.PrioritizeEvent(def,
                    $"Curated story (after {beat.QuestBool}): {Units.TicksToTime(ctx.Ticks)}");
            }
            return true;
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            upcomingEvents.Clear();
            milestoneBeats.Clear();
            if (Def.Vars.TryStringSet(PropertyIdH.EventSchedule, out var sched)) {
                foreach (var item in sched) {
                    TryParse(item);
                }
            }
            if (isLoading) {
                // A save from before these beats existed, loaded past a beat's ceiling, has
                // no state for it. That opening is over, so the beat does not fire late
                var data = ctx.AspectData;
                var ticks = ctx.S.Ticks;
                foreach (var beat in milestoneBeats) {
                    if (ticks >= beat.CeilingTick
                            && data.GetInt(beat.DueKey, 0) == 0
                            && !data.GetBool(beat.DoneKey, false)) {
                        data.SetBool(beat.DoneKey, true);
                    }
                }
            }
        }

        private void TryParse(string item) {
            var comp = item.Split(",");
            if (comp.Length < 2) {
                D.Err("Invalid configuration in curated schedule, format is days(float),event_id(string),flags(optional string): {0}",
                    item);
                return;
            }
            if (!float.TryParse(comp[0], out var days)) {
                D.Err("Invalid days (float) in curated schedule: {0}", item);
                return;
            }
            var def = StoryEventDef.Get(comp[1]);
            if (def == null) {
                D.Err("Invalid story event def id in curated schedule: {0}", item);
                return;
            }
            long ticks = (long) (days * Consts.TicksPerDay);
            bool forceStart = false;
            string afterBool = null;
            float afterDays = 0f;
            for (int i = 2; i < comp.Length; i++) {
                if (comp[i].StartsWith(AfterPrefix, StringComparison.Ordinal)) {
                    if (!TryParseAfter(comp[i], out afterBool, out afterDays)) {
                        D.Err("Invalid after tag in curated schedule, format is after:QuestBool+days: {0}",
                            item);
                        return;
                    }
                    continue;
                }
                switch (comp[i]) {
                    case "force_start": {
                        forceStart = true;
                        break;
                    }
                    default: {
                        D.Err("Unhandled tag {0} in curated story config: {1}",
                            comp[i], item);
                        break;
                    }
                }
            }
            if (afterBool != null) {
                // An OnDemand def is started directly by a milestone beat, so force_start is
                // allowed there
                milestoneBeats.Add(new MilestoneBeat {
                    Def = def,
                    ForceStart = forceStart,
                    QuestBool = afterBool,
                    OffsetTicks = (long) (afterDays * Consts.TicksPerDay),
                    CeilingTick = ticks,
                    DueKey = $"Curated.{def.Id}@{comp[0]}.Due",
                    DoneKey = $"Curated.{def.Id}@{comp[0]}.Done",
                });
                return;
            }
            if (forceStart && def.Flags.HasFlag(StoryEventFlags.OnDemand)) {
                D.Err("Misconfigured curated scheduled event, force_start but event is OnDemand: {0}",
                    item);
                forceStart = false;
            }
            if (!forceStart && def.Require != null) {
                if (def.Require.Days > ticks / Consts.TicksPerDay) {
                    D.Err("Misconfigured curated scheduled event: {0} - min days is {1} but scheduled at {2} without force_start. {3}",
                        def.Id, def.Require.Days, Units.TicksToTime(ticks), item);
                }
            }
            for (int i = 0; i < MaxTickCollisionRetries; i++) {
                if (!upcomingEvents.TryAdd(ticks, (def, forceStart))) {
                    ticks++;
                } else {
                    #if DEBUG_CURATED
                    D.Warn("Scheduled curated event: {0}->{1}",
                        Units.TicksToTime(ticks), def.Id);
                    #endif
                    return;
                }
            }
            D.Err("Collision in upcoming event: {0}. Could not add: {1} | {2}",
                item, ticks, D.DumpCollection(upcomingEvents));
        }

        // "after:HasRadar+0.25" -> ("HasRadar", 0.25)
        private static bool TryParseAfter(string tag, out string questBool, out float days) {
            questBool = null;
            days = 0f;
            var body = tag.Substring(AfterPrefix.Length);
            var plus = body.IndexOf('+');
            if (plus <= 0) {
                return false;
            }
            questBool = body.Substring(0, plus);
            return float.TryParse(body.Substring(plus + 1), NumberStyles.Float,
                CultureInfo.InvariantCulture, out days) && days >= 0f;
        }

        public override void Unload(StoryContext ctx) {
            upcomingEvents.Clear();
            milestoneBeats.Clear();
        }
    }
}
