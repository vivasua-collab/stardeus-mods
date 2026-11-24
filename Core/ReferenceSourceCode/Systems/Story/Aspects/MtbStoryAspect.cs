using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Story {
    // Memoryless story pacing: no calm / eventful phases. Every hour rolls a fixed chance sized
    // so that the mean gap between rolls is MtbDays (mean time between), then a weighted outcome
    // draw picks what kind of event fires. Exponential gaps are the point: several events can
    // land in one day and then nothing for a week, which is what makes the generator feel
    // chaotic without firing more often or breaking event rules. Raids are not rolled here;
    // RaidStoryAspect keeps its own wealth-keyed cadence, strength jitter and extensions, but
    // their window is still kept open here (see openOutcomes).
    public sealed class MtbStoryAspect : StoryAspect, ISaveableChild {
        [SelfInit]
        private static void Register() {
            Register(AspectId, Factory);
        }

        private static StoryAspect Factory() {
            return new MtbStoryAspect();
        }

        public const string AspectId = "Mtb";
        public override string Id => AspectId;

        private static readonly StoryEventOutcome[] outcomes = {
            StoryEventOutcome.Positive,
            StoryEventOutcome.Neutral,
            StoryEventOutcome.Negative,
        };
        // Every window this aspect keeps open. Raid is opened but never rolled: the raid pick
        // ignores the window, but it is priced against the Raid MaxPointAllowance, which
        // PointAllowanceStoryAspect only computes while the window is open. Left closed, every
        // raid def was priced out and Chaos sent no scheduled raids at all (rc291-rc313)
        private static readonly StoryEventOutcome[] openOutcomes = {
            StoryEventOutcome.Positive,
            StoryEventOutcome.Neutral,
            StoryEventOutcome.Negative,
            StoryEventOutcome.Raid,
        };
        private readonly float[] weights = new float[outcomes.Length];
        private readonly StoryEventOutcome[] drawOrder = new StoryEventOutcome[outcomes.Length];
        private readonly float[] drawWeights = new float[outcomes.Length];
        private float mtbDays;
        private long firstRollTick;
        private long lastRefreshDay;

        private string DataKey(string key) => $"{Def.Id}.{key}";

        public void OnSave(ComponentData data) {
            data.SetLong(DataKey("firstRoll"), firstRollTick);
        }

        public void OnLoad(ComponentData data) {
            // StorySys calls this on a new game too, with empty data, right after Initialize,
            // so a missing key has to keep what Initialize computed
            firstRollTick = data.GetLong(DataKey("firstRoll"), firstRollTick);
        }

        /// <summary>
        /// Chance that one hourly check fires so that the mean gap between fires is mtbDays:
        /// 1 - e^(-1 / (mtbDays * 24)). A true mean with exponentially distributed gaps.
        /// </summary>
        public static float ChancePerHour(float mtbDays) {
            if (mtbDays <= 0f) { return 1f; }
            return 1f - (float) Math.Exp(-1d / (mtbDays * 24d));
        }

        public override void OnTick(StoryContext ctx) {
            if (!ctx.IsEveryHour) { return; }
            KeepSchedulesOpen(ctx);
            if (ctx.Ticks < firstRollTick) { return; }
            var mtb = mtbDays;
            var pace = ModifierStoryPace.Current;
            if (pace > 0f) {
                // Same direction as the schedule cooldowns: faster pace, shorter mean gap
                mtb /= pace;
            }
            if (!ctx.Rng.Chance(ChancePerHour(mtb))) { return; }
            TryStartOne(ctx);
        }

        // No Schedule aspect runs alongside this one, so nobody else would ever open the
        // windows. Open windows keep hook events that respect the schedule working, and the
        // daily PointAllowance refresh keeps MaxPointAllowance current, which is the
        // affordability gate the picks below use (they pick with StoryPickIgnore.Schedule,
        // the same way raids do, so points never act as a depleting budget here).
        private void KeepSchedulesOpen(StoryContext ctx) {
            var day = ctx.Ticks / Consts.TicksPerDay;
            var refresh = day != lastRefreshDay;
            lastRefreshDay = day;
            for (int i = 0; i < openOutcomes.Length; i++) {
                OpenSchedule(ctx.Schedule[(int) openOutcomes[i]], refresh);
            }
        }

        private static void OpenSchedule(StorySchedule sch, bool refreshPoints) {
            sch.IsAllowed = true;
            sch.ChangeAfter = 0;
            sch.MaxEvents = 0;
            if (refreshPoints) {
                sch.PointAllowance = -1;
            }
        }

        private void TryStartOne(StoryContext ctx) {
            DrawOrder(ctx);
            for (int i = 0; i < drawOrder.Length; i++) {
                if (TryStart(ctx, drawOrder[i])) {
                    return;
                }
            }
            // Nothing affordable or off cooldown in any outcome: the roll is lost, which is
            // also what a random storyteller does when the drawn category has nothing allowed
        }

        // Weighted draw without replacement: the first outcome is the drawn one, the rest are
        // fallbacks in draw order.
        private void DrawOrder(StoryContext ctx) {
            for (int i = 0; i < outcomes.Length; i++) {
                drawOrder[i] = outcomes[i];
                drawWeights[i] = weights[i];
            }
            for (int pos = 0; pos < outcomes.Length; pos++) {
                var total = 0f;
                for (int i = pos; i < outcomes.Length; i++) {
                    total += drawWeights[i];
                }
                if (total <= 0f) { break; }
                var r = ctx.Rng.Range(0f, total);
                var pick = pos;
                var acc = 0f;
                for (int i = pos; i < outcomes.Length; i++) {
                    acc += drawWeights[i];
                    if (r < acc) {
                        pick = i;
                        break;
                    }
                }
                (drawOrder[pos], drawOrder[pick]) = (drawOrder[pick], drawOrder[pos]);
                (drawWeights[pos], drawWeights[pick]) = (drawWeights[pick], drawWeights[pos]);
            }
        }

        private bool TryStart(StoryContext ctx, StoryEventOutcome outcome) {
            var handler = ctx.S.Query.StoryEventHandlerFor.Ask(outcome);
            if (handler == null) { return false; }
            var potential = Caches.WeightedEvents;
            potential.Clear();
            foreach (var evt in StoryEventDef.Considerable[(int) outcome]) {
                var w = handler.GetEventWeight(ctx, evt, StoryPickIgnore.Schedule);
                if (w > 0f) {
                    potential.Add(evt, w);
                }
            }
            if (potential.Count == 0) { return false; }
            var data = handler.TryStartPotential(ctx, potential, 0f);
            return data != null && data.State != StoryEventState.Failed;
        }

        public override void Initialize(StoryContext ctx, bool isLoading) {
            mtbDays = Def.Vars.Get(PropertyIdH.MtbDays, 3f);
            weights[0] = Def.Vars.Get(PropertyIdH.WeightPositive, 1f);
            weights[1] = Def.Vars.Get(PropertyIdH.WeightNeutral, 1f);
            weights[2] = Def.Vars.Get(PropertyIdH.WeightNegative, 1f);
            // Aspect instances are shared by every game played in a session
            // (StoryGenDef.LinkAspects), so nothing may carry over from the previous one
            lastRefreshDay = -1L;
            if (!isLoading) {
                var graceDays = Def.Vars.Get(PropertyIdH.InitialExtraDelayDays, 0f);
                firstRollTick = ctx.Ticks + (long) (graceDays * Consts.TicksPerDay);
            } else {
                // OnLoad restores it; a save without the key gets no grace
                firstRollTick = 0L;
            }
            // Open the windows now rather than on the first hourly tick, so a save made under
            // a phased generator (or an older Chaos) is live immediately
            for (int i = 0; i < openOutcomes.Length; i++) {
                var sch = ctx.Schedule[(int) openOutcomes[i]];
                OpenSchedule(sch, true);
                sch.CooldownUntil = 0;
                sch.Executed = 0;
            }
        }

        public override void Unload(StoryContext ctx) {
        }
    }
}
