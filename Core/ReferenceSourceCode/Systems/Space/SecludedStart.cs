using System.Collections.Generic;
using Game.Constants;
using KL.Utils;

namespace Game.Systems.Space {
    // Scenarios that send the player to the derelict next to them (Scenario.StartsAlone) start
    // alone in their sector. Faction ownership, stations, derelicts and ships can be stripped
    // before the sector is generated, but a port cannot - it is faction infrastructure and can
    // be a quest target - so a region with no port-less sector cannot host such a start.
    // Works on biome data only, so the setup screen can ask before any SpaceObject exists.
    public static class SecludedStart {
        public static SpaceSector Find(SpaceRegion region) {
            SpaceSector best = null;
            foreach (var sec in region.Sectors) {
                if (!IsCandidate(sec)) { continue; }
                if (best == null || IsBetter(sec, best)) {
                    best = sec;
                }
            }
            return best;
        }

        public static bool IsPossibleIn(SpaceRegion region) {
            return Find(region) != null;
        }

        // Must run before the sector is generated, the generators read these
        public static void Strip(SpaceSector sector) {
            var biome = sector.Biome;
            biome.FactionsByDominance = null;
            List<POIConfig> shipPOIs = null;
            foreach (var poi in biome.POIs.Keys) {
                if (poi.ProducesShip) {
                    shipPOIs ??= new List<POIConfig>();
                    shipPOIs.Add(poi);
                }
            }
            if (shipPOIs == null) { return; }
            foreach (var poi in shipPOIs) {
                biome.POIs.Remove(poi);
            }
        }

        private static bool IsCandidate(SpaceSector sec) {
            if (!StartingSector.IsAllowed(sec)) { return false; }
            if (sec.Biome.Template is not SectorTemplate tpl) { return false; }
            if (tpl.CantBeInbound || tpl.ContentIsShips) { return false; }
            int keptPOIs = 0;
            foreach (var poi in sec.Biome.POIs.Keys) {
                if (poi.Type == SpaceObjectTypeId.Port) { return false; }
                if (!poi.ProducesShip) {
                    keptPOIs++;
                }
            }
            // A planet or asteroid zone with nothing left in it after stripping fails to
            // generate its contents
            if (tpl.MustHavePOIs && keptPOIs == 0 && sec.Biome.Deposits.Count == 0) {
                return false;
            }
            return true;
        }

        // Same preferences as the regular start (no space effects, then the most deposits
        // for early mining), then the least to strip
        private static bool IsBetter(SpaceSector a, SpaceSector b) {
            var cmp = StartingSector.Compare(a, b);
            if (cmp != 0) { return cmp > 0; }
            return StripWork(a) < StripWork(b);
        }

        private static int StripWork(SpaceSector sec) {
            var work = Arrays.IsEmpty(sec.Biome.FactionsByDominance) ? 0 : 1;
            foreach (var poi in sec.Biome.POIs) {
                if (poi.Key.ProducesShip) {
                    work += poi.Value;
                }
            }
            return work;
        }
    }
}
