using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Systems;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class StartingResourcesGenStage : BaseShipGenStage {
        private readonly string scenario;

        public StartingResourcesGenStage(string scenario) {
            this.scenario = scenario;
        }
        public override void Apply() {
            var forkedRng = rng.Fork();

            var mats = new Dictionary<MatType, int>();
            var startingMats = new Dictionary<MatType, int>();

            ScenarioStartingMats.Load(scenario, S.Difficulty.StartingResources,
                startingMats);

            // The whole scenario budget, not the scattered share of it: this stage tops the
            // ship up to what the scenario promises and counts everything already aboard,
            // storage units included. That is what makes the split with a storage stage
            // self-correcting - a material the storage units refuse by group (food, with no
            // fridge aboard yet) spills onto the floor and is simply not topped up twice,
            // and neither stage has to agree with the other about rounding. Scaling this
            // target down by the scatter share instead left every split scenario holding
            // only the stored half of each material.
            S.Sys.Inventory.LoadRemainingMaterials(mats);
            var ars = S.Sys.Areas;

            foreach (var mat in startingMats) {
                var matType = mat.Key;
                var amt = mats.Get(matType, 0);
                int attempts = 100000;
                var target = mat.Value;
                var found = S.Components.FindAllMatching(Caches.Piles, m => m.Type == matType);
                int pileIdx = 0;
                while (found > 0 && amt > target * 1.1f && attempts-- > 0) {
                    if (pileIdx >= found) {
                        pileIdx = 0;
                        found = S.Components.FindAllMatching(
                            Caches.Piles, m => m.Type == matType && m.StackSize > 0);
                    }
                    var pile = Arrays.SafeGet(Caches.Piles, pileIdx++);
                    if (pile != null && pile.StackSize > 0) {
                        var toTake = Mathf.RoundToInt(pileIdx % 2 == 0 ? pile.StackSize / 2f
                            + 1 : pile.StackSize / 2f);
                        if (toTake > 0) {
                            amt -= pile.Take(matType, null, toTake, null);
                        }
                    }
                }
                attempts = 10000;
                int amtMin = Mathf.CeilToInt(target * 0.01f);
                int amtMax = Mathf.CeilToInt(target * 0.03f);
                while (amt < target && attempts-- > 0) {
                    amt += SpawnResource(matType, amtMin, amtMax, ars, forkedRng);
                }
            }
        }

        // A pile gets a tile of its own while the ship has room, so none hides another
        // (STA-1787). Wrecked Empty Ship scatters its whole budget, which can outgrow the free
        // floor, and then a pile goes on any empty floor as before.
        private const int OwnTileAttempts = 50;
        private const int MaxAttempts = 10000;

        private int SpawnResource(MatType mat, int amtMin, int amtMax, AreaSys area, Rng rng) {
            int pos;
            for (int attempt = 0; ; attempt++) {
                pos = area.GenRandomSolidPos(rng);
                var isGood = attempt < OwnTileAttempts
                    ? EntityUtils.IsClearFloorAt(pos)
                    : EntityUtils.IsEmptyFloorWithIntegrityAt(pos);
                if (isGood || attempt >= MaxAttempts) { break; }
            }
            if (pos == Pos.Invalid) {
                return 0;
            }

            var amt = rng.Range(amtMin, amtMax);

            var pile = EntityUtils.SpawnRawMaterial(mat, amt, pos, 0.5f);
            if (pile != null) {
                return amt;
            }
            return 0;
        }
    }
}
