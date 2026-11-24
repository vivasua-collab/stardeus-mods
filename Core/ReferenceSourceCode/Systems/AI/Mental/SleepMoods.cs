using Game.Constants;

namespace Game.Systems.Mental {
    // The moods a night gives say what the bed and the room were like. The room moods of a well
    // built ship last until the next wake-up, which judges the night again, so last night's
    // moods end first: after a night on a sofa, yesterday's cozy room must be gone, not
    // carried for the rest of the day next to "Uncomfortable Sleep".
    public static class SleepMoods {
        public static void EndLastNight(MentalHealth health, long ticks) {
            if (health == null) { return; }
            var effects = health.CurrentEffects;
            for (int i = effects.Count - 1; i >= 0; i--) {
                var e = effects[i];
                // the moods this wake-up gives again replace these in place, the rest leave
                // the list on the next tick (MentalHealth.ProcessMoodEffects)
                if (!e.IsPermanent && e.EndTick > ticks && IsSleepMood(e.Reason)) {
                    e.EndTick = ticks;
                }
            }
        }

        // Matched by the reason text, the way AddEffect replaces a timed effect. T is not
        // cached: a language change replaces its strings.
        private static bool IsSleepMood(string reason) {
            return reason == T.SleepEffOwnRoom
                || reason == T.SleepEffCozyRoom
                || reason == T.SleepEffDorm
                || reason == T.SleepEffRoomTooSmall
                || reason == T.SleepEffRoomTooLarge
                || reason == T.SleepEffIndustrial
                || reason == T.SleepEffIndustrialNoisy
                || reason == T.SleptInBed
                || reason == T.UncomfortableSleep;
        }
    }
}
