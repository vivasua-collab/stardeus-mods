using Game.Constants;

namespace Game.Systems.Space {
    // Every start - new game or a regenerated universe - is a planet or an asteroid zone. They
    // are the only sectors that turn biome deposits into POIs, so the first expedition always
    // has somewhere to go. Works on biome data only, so the setup screen can ask before any
    // SpaceObject exists.
    public static class StartingSector {
        public static bool IsAllowed(SpaceSector sec) {
            return sec.Biome.Template is SectorTemplate tpl
                && (tpl.Type == SpaceObjectTypeId.Planet
                    || tpl.Type == SpaceObjectTypeId.AsteroidZone);
        }

        public static SpaceSector Find(SpaceRegion region) {
            SpaceSector best = null;
            foreach (var sec in region.Sectors) {
                if (!IsAllowed(sec)) { continue; }
                if (best == null || Compare(sec, best) > 0) {
                    best = sec;
                }
            }
            return best;
        }

        public static bool IsPossibleIn(SpaceRegion region) {
            return Find(region) != null;
        }

        // Positive when a is the better start: no space effects first, then the most deposits
        // for early mining. Ties keep the earlier sector, as the pick always has, so starts that
        // were already a planet or an asteroid zone did not move
        public static int Compare(SpaceSector a, SpaceSector b) {
            var aCalm = a.Biome.Effects.Count == 0;
            var bCalm = b.Biome.Effects.Count == 0;
            if (aCalm != bCalm) { return aCalm ? 1 : -1; }
            return a.Biome.Deposits.Count.CompareTo(b.Biome.Deposits.Count);
        }

        // The start is picked for being calm and rich, not for what it holds, so it can offer
        // nothing but deposits a new player has never heard of (Phasium, Soil) and the first
        // expedition brings back nothing that connects to what they build. Iron always does.
        // Added to the biome before the sector is generated, so the deposit goes through regular
        // generation and starts Unknown like the rest - the player may not have a radar yet.
        public static void EnsureIron(SpaceSector sector) {
            // Only reached through GameState's error fallback: the starmap would count a
            // deposit that never spawns
            if (!IsAllowed(sector)) { return; }
            var biome = sector.Biome;
            var iron = DepositConfig.Get(MatId.Iron);
            if (biome.HasDepositsFor(iron.Mat)) { return; }
            biome.AddAssignable(iron);
        }
    }
}
