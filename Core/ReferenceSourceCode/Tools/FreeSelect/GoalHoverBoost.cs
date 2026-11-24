using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Utils;
using KL.Grid;

namespace Game.Tools {
    // Hidden convenience: resting the cursor on an unassigned task for a few seconds boosts
    // it (same mechanic as new player tasks / the Retry UI), so a worker picks it up sooner.
    // Continued hovering re-fires every DwellSeconds, prolonging a boost about to expire.
    public sealed class GoalHoverBoost {
        private const double GapSeconds = 0.25d;
        private static double DwellSeconds => Tunables.FreeSelectGoalHoverBoostDelay;

        private int lastPos = Pos.Invalid;
        private double lastSeen;
        private double dwell;
        private bool retryUsed;

        public void Update(FreeSelectContext ctx) {
            var now = GameTimer.realtimeSinceStartupAsDouble;
            var pos = ctx.HoveredPos;
            // a task under a roof cannot be seen, so resting on the roof boosts nothing
            if (ctx.HoveredGoals.Count == 0 || Pos.IsOOB(pos) || ctx.IsUnderRoof) {
                EndEpisode(pos, now);
                return;
            }
            if (pos != lastPos || now - lastSeen > GapSeconds) {
                EndEpisode(pos, now);
            } else {
                dwell += now - lastSeen;
                lastSeen = now;
            }
            if (dwell < DwellSeconds) { return; }
            dwell = 0d;
            BoostHoveredGoals(ctx);
        }

        private void EndEpisode(int pos, double now) {
            lastPos = pos;
            lastSeen = now;
            dwell = 0d;
            retryUsed = false;
        }

        private void BoostHoveredGoals(FreeSelectContext ctx) {
            var ticks = G.Ticks;
            var goals = ctx.HoveredGoals;
            for (int i = goals.Count - 1; i >= 0; i--) {
                var goal = goals[i];
                if (goal.Type != AIGoalType.Colony) { continue; }
                if (goal.Agent != null) { continue; }
                if (goal.GoalPriority == 0) { continue; }
                if (!retryUsed) {
                    // TryRetry boosts regardless of state; it only force-retries a
                    // Failed/Error/ErrorLooping goal, and only then does this dwell
                    // episode's single retry get spent
                    if (AIGoal.TryRetry(goal, ticks, true, true)) {
                        retryUsed = true;
                    }
                } else {
                    goal.Boost();
                }
            }
        }
    }
}
