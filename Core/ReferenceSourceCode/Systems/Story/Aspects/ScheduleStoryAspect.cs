#if !PRODUCTION_BUILD
// #define DEBUG_SCHEDULE
#endif
using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Procgen;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class ScheduleStoryAspect : StoryAspect {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new ScheduleStoryAspect();
        }

        public const string AspectId = "Schedule";
        public override string Id => AspectId;
        public StoryEventOutcome Outcome => outcome;
        private StoryEventOutcome outcome;
        private int outcomeIdx;
        private float cooldownMul;

        private Curve curveOn;
        private Curve curveOff;
        // Optional. Days until this outcome's first window opens on a new game, rolled once at
        // game start. Without it the first window is an ordinary OFF roll, which is what the
        // Raid schedule wants (RaidStoryAspect opens it on its own cooldown anyway).
        private Curve initialDelayCurve;
        // Optional. Periodic events allowed per ON window, rolled on every flip to ON. Without
        // it a window is bounded only by points and the picker cooldown, and a window full of
        // cheap events fires until the budget is gone.
        private Curve eventsPerCycleCurve;
        // Optional. The first window opens this many days after the quest bool is set, if
        // that is sooner than the opening delay above, which stays the latest it can open.
        private string openAfterQuestBool;
        private Curve openAfterDaysCurve;
        private string openedEarlyKey;

        public override void OnTick(StoryContext ctx) {
            var ticks = ctx.Ticks;
            var schedule = ctx.Schedule[outcomeIdx];
            if (ctx.IsEveryHour && openAfterDaysCurve != null) {
                TryOpenEarly(ctx, schedule);
            }
            if (schedule.ChangeAfter < ticks) {
                if (schedule.IsAllowed) {
                    schedule.IsAllowed = false;
                    schedule.ChangeAfter = ChangeAfter(ctx, curveOff, true);
                    #if DEBUG_SCHEDULE
                    D.Warn("Changed schedule to OFF: {0}", schedule);
                    #endif
                } else {
                    schedule.IsAllowed = true;
                    // reset number of executed events this cycle
                    schedule.Executed = 0;
                    schedule.MaxEvents = RollMaxEvents(ctx);
                    schedule.Cycle++;
                    schedule.ChangeAfter = ChangeAfter(ctx, curveOn, false);
                    // if it's negative, the PointAllowanceStoryAspect will
                    // refill the points.
                    schedule.PointAllowance = -1;
                    #if DEBUG_SCHEDULE
                    D.Warn("Changed schedule to ON: {0}", schedule);
                    #endif
                }
                ctx.S.Sig.StoryScheduleChanged.Send(schedule);
            }
        }

        // The opening waits for the player, not the calendar: its purpose is to let the
        // tutorial spine finish before random events start, and a fast player finishes it in
        // a third of the time. Once the milestone bool is set, the first window opens after a
        // short offset. Games without the tutorial chain never set the bool and keep the fixed
        // opening day, like the first stasis wake-up. Loaded saves still in their opening
        // catch up through the same check.
        private void TryOpenEarly(StoryContext ctx, StorySchedule schedule) {
            if (schedule.Cycle != 0 || schedule.IsAllowed) { return; }
            if (ctx.AspectData.GetBool(openedEarlyKey, false)) { return; }
            if (!ctx.S.Query.GetQuestBool.Ask(openAfterQuestBool)) { return; }
            // Rolled once, so the check does not draw story rng every hour
            ctx.AspectData.SetBool(openedEarlyKey, true);
            var due = ChangeAfter(ctx, openAfterDaysCurve, true);
            if (due < schedule.ChangeAfter) {
                D.Warn("{0} window opens early after {1}: in {2} instead of {3}",
                    outcome, openAfterQuestBool, Units.TicksToTime(due - ctx.Ticks),
                    Units.TicksToTime(schedule.ChangeAfter - ctx.Ticks));
                schedule.ChangeAfter = due;
            }
        }

        private int RollMaxEvents(StoryContext ctx) {
            if (eventsPerCycleCurve == null) { return 0; }
            return ctx.Rng.IntCurve(eventsPerCycleCurve);
        }

        private float ScaleCooldownDays(float delayDays) {
            var pace = ModifierStoryPace.Current;
            if (pace <= 0f) { pace = 1f; }
            return delayDays * cooldownMul / pace;
        }

        private long DelayTicks(StoryContext ctx, Curve curve, bool isCooldown) {
            var delayDays = ctx.Rng.Curve(curve);
            if (isCooldown) {
                delayDays = ScaleCooldownDays(delayDays);
            }
            return (long) (delayDays * Consts.TicksPerDay);
        }

        private long ChangeAfter(StoryContext ctx, Curve curve, bool isCooldown) {
            return ctx.Ticks + DelayTicks(ctx, curve, isCooldown);
        }

        /// <summary>
        /// Read-only [min, max] of the opening delay in days, before difficulty / pace scaling.
        /// (0, 0) when this schedule has no InitialDelayDaysCurve. For reports and tests.
        /// </summary>
        public (float from, float to) PreviewInitialDelayRangeDays() {
            if (initialDelayCurve == null) { return (0f, 0f); }
            return (initialDelayCurve.Evaluate(0f), initialDelayCurve.Evaluate(1f));
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            outcome = ParseOutcome();
            // see StoryEventOutcome, the order is Negative, Positive, Neutral, Raid
            outcomeIdx = (int) outcome;
            LoadCurve(PropertyIdH.DaysCurve_On, ref curveOn);
            LoadCurve(PropertyIdH.DaysCurve_Off, ref curveOff);
            LoadOptionalCurve(PropertyIdH.InitialDelayDaysCurve, ref initialDelayCurve);
            LoadOptionalCurve(PropertyIdH.EventsPerCycleCurve, ref eventsPerCycleCurve);
            openAfterQuestBool = Def.Vars.Get(PropertyIdH.OpenAfterQuestBool, string.Empty);
            openAfterDaysCurve = null;
            if (!string.IsNullOrEmpty(openAfterQuestBool)) {
                LoadCurve(PropertyIdH.OpenAfterDaysCurve, ref openAfterDaysCurve);
            }
            openedEarlyKey = $"Schedule.{outcome}.OpenedEarly";

            var schedule = ctx.Schedule[outcomeIdx];
            cooldownMul = schedule.DifficultyMod.CooldownMul;
            if (cooldownMul != 1f) {
                D.Warn("Schedule Cooldown Mul for difficulty {0}: {1}",
                    ctx.S.Difficulty.Id, cooldownMul);
            }
            if (cooldownMul == 0f) {
                D.Err("Schedule cooldown multiplier was set to 0! Fixing");
                cooldownMul = 1f;
            }

            if (isLoading) {
                CatchUpLoadedSchedule(ctx, schedule);
                return;
            }

            if (schedule.ChangeAfter == 0) {
                schedule.ChangeAfter = ChangeAfter(ctx, initialDelayCurve ?? curveOff, true);
            }
        }

        // Saves made before the per-window cap and the explicit opening delay existed carry
        // states a new game never produces. They are repaired here, by state, rather than in a
        // versioned Migration: every v0.15.0-rc save parses to the same version, and the right
        // values come from this aspect's own curves, which only exist once it initializes.
        private void CatchUpLoadedSchedule(StoryContext ctx, StorySchedule schedule) {
            if (schedule.IsAllowed && schedule.MaxEvents == 0 && eventsPerCycleCurve != null) {
                // Executed used to count finalized events of the outcome, hook events included,
                // so the loaded value must not be held against the fresh cap
                schedule.Executed = 0;
                schedule.MaxEvents = RollMaxEvents(ctx);
                D.Warn("Story schedule catch-up: capped the open {0} window at {1} events",
                    outcome, schedule.MaxEvents);
            }
            // A window pulled in by its milestone (TryOpenEarly) is meant to open before the
            // opening delay, so it must not be held back to it
            if (schedule.Cycle == 0 && !schedule.IsAllowed && initialDelayCurve != null
                    && !ctx.AspectData.GetBool(openedEarlyKey, false)) {
                // The first window never opened, so the save is still in the opening. Hold it
                // to at least the configured opening delay, measured from game start (tick 0).
                // Only ever moves the flip later, and only while it is below the scaled
                // minimum, so a second load leaves it alone.
                var minTicks = (long) (ScaleCooldownDays(initialDelayCurve.Evaluate(0f))
                    * Consts.TicksPerDay);
                if (schedule.ChangeAfter < minTicks) {
                    var rolled = DelayTicks(ctx, initialDelayCurve, true);
                    D.Warn("Story schedule catch-up: {0} first window moved from {1} to {2}",
                        outcome, Units.TicksToTime(schedule.ChangeAfter),
                        Units.TicksToTime(rolled));
                    schedule.ChangeAfter = Math.Max(schedule.ChangeAfter, rolled);
                }
            }
        }

        public override void Unload(StoryContext ctx) {
        }
    }
}
