using System.Linq;
using Game.CodeGen;
using Game.Data.Space;
using Game.Scenarios;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Space {
    // The one rule for where a fresh start goes: the setup screen's default, Quick Start and a
    // regenerated universe. The easiest band (green on the starmap) comes first. Up to
    // MaxDifficulty is also easy, but it shows light blue, and a light blue default reads as the
    // game passing over the green regions (STA-1751). Generation only promises regions up to
    // MaxDifficulty (GenSpaceBiome.EnforceDifficultyDistribution); in practice the easiest one
    // sits at 0, and a harder universe (difficulty slider above Default) may start light blue.
    public static class StartingRegion {
        public const float MaxDifficulty = 0.25f;

        public static SpaceRegion PickRandom(SpaceMap map, Scenario scenario, Rng rng) {
            var start = map.Regions
                .Where(r => r.Biome.IsEasiest && IsEasyStart(r, scenario))
                .OrderBy(r => rng.Value)
                .FirstOrDefault();
            start ??= map.Regions
                .Where(r => r.Biome.Difficulty <= MaxDifficulty && IsEasyStart(r, scenario))
                .OrderBy(r => rng.Value)
                .FirstOrDefault();
            if (start != null) { return start; }
            start = map.Regions
                .Where(r => SOUtils.HasHyperjumpRelay(r) && CanStartIn(r, scenario))
                .OrderBy(r => r.Biome.Difficulty)
                .FirstOrDefault();
            D.Err("No easy starting region, fallback: {0}", start);
            return start;
        }

        // Regions without a relay are rejected separately, with their own explanation
        public static bool CanStartIn(SpaceRegion region, Scenario scenario) {
            return scenario?.StartsAlone == true
                ? SecludedStart.IsPossibleIn(region)
                : StartingSector.IsPossibleIn(region);
        }

        // Iron is checked in the sectors: the region's own deposit list is only what it rolled at
        // its hardest sector's chance, while easy regions get Iron forced into their sectors
        private static bool IsEasyStart(SpaceRegion region, Scenario scenario) {
            return SOUtils.HasHyperjumpRelay(region)
                && HasIron(region)
                && CanStartIn(region, scenario);
        }

        private static bool HasIron(SpaceRegion region) {
            foreach (var sec in region.Sectors) {
                foreach (var dep in sec.Biome.Deposits.Keys) {
                    if (dep.Mat.IdH == MatIdH.Iron) { return true; }
                }
            }
            return false;
        }
    }
}
