using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Data;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine.InputSystem;

namespace Game.Procgen {
    public class AbandonSectionsGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        public override bool ProducesIslands => true;
        private readonly int minSections;
        private readonly int maxSections;
        private readonly int minTilesRemaining;
        public AbandonSectionsGenStage(int minSections, int maxSections,
                int minTilesRemaining) {
            this.minSections = minSections;
            this.maxSections = maxSections;
            this.minTilesRemaining = minTilesRemaining;
        }

        public override IEnumerator ApplyAsync() {
            var subRng = rng.Fork();
            var wantedSections = subRng.Range(minSections, maxSections + 1);
            var islands = S.Sys.Areas.Islands.Values.OrderBy(v => v.Count).ToList();
            int islandCount = islands.Count;
            var totalTiles = islands.Sum(i => i.Count);

            for (int i1 = 0; i1 < islands.Count; i1++) {
                HashSet<int> i = islands[i1];
                if (totalTiles < minTilesRemaining) { break; }
                if (islandCount <= wantedSections) { break; }
                var iCount = i.Count;
                // get rid of shitlets
                if (iCount > 30) {
                    if (totalTiles - iCount < minTilesRemaining) { continue; }
                    if (subRng.Chance(0.5f)) { continue; }
                }
                if (TryAbandon(i)) {
                    totalTiles -= iCount;
                    if (!GameTimer.HasAvailableFrameBudget()) {
                        yield return null;
                    }
                }
            }
        }

        private bool TryAbandon(HashSet<int> island) {
            if (!IsWithinGenArea(island.First())) {
                return false;
            }
            if (spec.HasCPU && island.Contains(spec.CPUPos)) {
                return false;
            }
            if (spec.HasStasisArray) {
                foreach (var sp in spec.StasisPos) {
                    if (island.Contains(sp)) {
                        return false;
                    }
                }
            }

            if (spec.InvincibleParts?.Overlaps(island) == true) {
                return false;
            }
            new CmdAbandonSection(island, true).Execute(S);
            return true;
        }
    }
}
