using Game.Constants;
using Game.Systems.Creatures;
using UnityEngine;

namespace Game.Systems.Mental {
    public static class FunNovelty {
        // Candidates this close to the best novelty count as equally good, so distance decides
        public const float PreferenceTolerance = 0.1f;
        // Keeps planner costs finite for a completely worn out source
        private const float MinCostNovelty = 0.1f;
        // MentalEffect.Create rejects shorter effects
        private const int MinMoodDuration = Consts.TicksPerHour;

        #if !PRODUCTION_BUILD
        public static int DebugPredictionScans;
        public static int DebugPredictionCandidates;
        #endif

        public static long WindowTicks =>
            (long) (Tunables.FunRepetitionWindowDays * Consts.TicksPerDay);

        // Novelty of a source during a session, cached per mind
        public static float Of(BeingData being, FunSource src, long ticks) {
            var health = being?.Mind?.Health;
            if (health == null || !src.IsValid) {
                return 1f;
            }
            return health.FunMemory.CachedNovelty(src, ticks, WindowTicks,
                being.Mind.Species?.FunRepetition ?? Species.DefaultFunRepetition);
        }

        // Novelty of a candidate source while predicting or choosing, never cached
        public static float Predict(BeingData being, FunSource src, long ticks) {
            #if !PRODUCTION_BUILD
            DebugPredictionCandidates++;
            #endif
            return Current(being, src, ticks);
        }

        // Uncached novelty for UI
        public static float Current(BeingData being, FunSource src, long ticks) {
            var health = being?.Mind?.Health;
            if (health == null || !src.IsValid) {
                return 1f;
            }
            return health.FunMemory.Novelty(src, ticks, WindowTicks,
                being.Mind.Species?.FunRepetition ?? Species.DefaultFunRepetition);
        }

        public static void Record(BeingData being, FunSource src, long ticks) {
            var health = being?.Mind?.Health;
            if (health == null || !src.IsValid) { return; }
            health.FunMemory.RecordUse(src, ticks, WindowTicks);
        }

        public static float AdjustCost(float baseCost, float novelty) {
            return baseCost / Mathf.Max(novelty, MinCostNovelty);
        }

        // An overused activity should feel both weaker and more fleeting, so the effect and
        // the duration both shrink with novelty (total impact falls roughly with novelty
        // squared). Small bonuses round away to nothing once they are worn out.
        public static bool TryScaleMood(int baseEffect, long baseDuration, float novelty,
                out int effect, out long duration) {
            if (novelty >= 0.999f) {
                effect = baseEffect;
                duration = baseDuration;
                return baseEffect != 0;
            }
            effect = 0;
            duration = 0;
            if (baseEffect <= 0 || baseDuration <= 0 || novelty <= 0f) {
                return false;
            }
            // round half up, RoundToInt would round 2.5 down to 2
            var e = Mathf.FloorToInt(baseEffect * novelty + 0.5f);
            if (e <= 0) {
                return false;
            }
            var d = (long) (baseDuration * novelty);
            if (d < MinMoodDuration) {
                d = MinMoodDuration;
            }
            if (d > baseDuration) {
                d = baseDuration;
            }
            effect = e;
            duration = d;
            return true;
        }
    }
}
