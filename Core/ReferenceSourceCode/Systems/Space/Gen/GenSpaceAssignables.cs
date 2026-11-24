using System;
using System.Collections.Generic;
using System.Linq;
using KL.Randomness;
using KL.Utils;
using UnityEngine.Pool;

namespace Game.Systems.Space {
    public abstract class GenSpaceAssignables<T> where T : SpaceAssignable {
        private readonly Dictionary<string, T> All;
        private readonly List<T> unassigned = new();
        public GenSpaceAssignables(Dictionary<string, T> all) {
            All = all;
        }
        public void AddUnassigned(List<T> list) {
            unassigned.AddRange(list);
        }
        public void Assign(SpaceMap map, Rng rng) {
            AssignRegions(map, rng.Fork());
        }

        protected abstract bool CanAssignLeftoversTo(SpaceSector sec);
        public void AssignLeftovers(SpaceMap map, Rng rng) {
            if (unassigned.Count == 0) { return; }
            // D.Warn("Have to assign leftovers: {0}", D.Dump(unassigned));
            var sectors = ListPool<SpaceSector>.Get();
            sectors.Clear();
            foreach (var r in map.Regions) {
                foreach (var s in r.Sectors) {
                    if (CanAssignLeftoversTo(s)) {
                        sectors.Add(s);
                    }
                }
            }
            int attempts = 2;
            while (unassigned.Count > 0 && attempts-- > 0) {
                foreach (var s in sectors.OrderBy(v => rng.Value)) {
                    for (int i = unassigned.Count - 1; i >= 0; i--) {
                        var unass = unassigned[i];
                        if (unass.Matches(s, out var diffChance)) {
                            if (rng.Chance(diffChance)) {
                                s.Biome.AddAssignable(unass);
                                unassigned.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
            }
            sectors.Clear();
            ListPool<SpaceSector>.Release(sectors);
            // if (unassigned.Count > 0) {
            //     D.Warn("Left with leftovers: {0}", D.Dump(unassigned));
            // }
        }

        private void AssignRegions(SpaceMap map, Rng rng) {
            // 1. get all deposits that have map quantity, assign them to regions
            List<T> rare = new();
            List<T> common = new();
            foreach (var d in All.Values.OrderBy(d => d.Id)) {
                var universeQ = rng.IntCurve(d.UniverseQuantityCurve);
                if (universeQ < 1) {
                    common.Add(d);
                    continue;
                }
                for (int i = 0; i < universeQ; i++) {
                    rare.Add(d);
                }
            }
            int attempts = 100;
            while (rare.Count > 0 && attempts-- > 0) {
                TryAssignRare(map, rng, rare);
            }
            AddUnassigned(rare);

            // 2. assign common deposits
            foreach (var reg in map.Regions) {
                AssignFor(reg, common, rng);
            }
        }

        /// <param name="diffShift">how much difficulty we will add to each region difficulty to shift it up so we can get more deposits</param>
        private void TryAssignRare(SpaceMap map, Rng rng, List<T> rare) {
            for (int i = rare.Count - 1; i >= 0; i--) {
                var dep = rare[i];
                var availCount = rare.Count(d => d.Id == dep.Id);
                if (AssignRare(dep, map, rng, availCount, out var assCount)) {
                    rare.RemoveAt(i);
                    if (assCount > 1) {
                        assCount--;
                        while (assCount-- > 0) {
                            rare.Remove(dep);
                        }
                        if (i >= rare.Count) {
                            break;
                        }
                    }
                }
            }
        }

        private void AssignFor(SpaceRegion reg, List<T> common, Rng rng) {
            foreach (var dep in common) {
                if (dep.Matches(reg, out var diffChance)) {
                    if (rng.Chance(diffChance)) {
                        var regQ = rng.IntCurve(dep.RegionQuantityCurve);
                        if (regQ > 0) {
                            reg.Biome.AddAssignable(dep, regQ);
                        }
                    }
                }
            }
        }

        private bool AssignRare(T cfg, SpaceMap map, Rng rng,
                int availableQuantity, out int assignedQuantity) {
            foreach (var reg in map.Regions.OrderBy(r => rng.Value)) {
                if (cfg.Matches(reg, out var diffChance)) {
                    if (reg.Biome.HasAssignable(cfg, true)) {
                        continue;
                    }
                    if (rng.Chance(diffChance)) {
                        assignedQuantity = rng.IntCurve(cfg.RegionQuantityCurve);
                        if (assignedQuantity > availableQuantity) {
                            assignedQuantity = availableQuantity;
                        }
                        reg.Biome.AddAssignable(cfg, assignedQuantity);
                        return true;
                    } else {
                        reg.Biome.AddUnavailableAssignable(cfg);
                    }
                }
            }
            assignedQuantity = 0;
            return false;
        }
    }
}
