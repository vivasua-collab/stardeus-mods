using Game.Data;

namespace Game.Systems.Items {
    // The Situation that item SpawnRequirements are checked against while inventory is
    // generated. It is the live S.Situation except while a raid spawns its raiders: raider gear
    // ignores the sector, Days and Friendlies gates and is capped by the raid's points instead
    // (CombatRaid.RaiderGearSituation), while traders, colonists, derelict crews and gifts keep
    // the plain situation.
    public static class ItemSpawnSituation {
        private static Situation overrideSituation;

        public static Situation For(GameState S) {
            return overrideSituation ?? S.Situation;
        }

        // Callers reset it with Set(null) in a finally block, so it never leaks into the next
        // inventory roll that is not theirs.
        public static void Set(Situation situation) {
            overrideSituation = situation;
        }
    }
}
