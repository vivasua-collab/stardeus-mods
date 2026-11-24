using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using Game.Constants;
using KL.Utils;
using Game.Commands;
using KL.Randomness;
using Game.Utils;
using Game.CodeGen;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Conditions;
using Game.Systems.Naming;

namespace Game.Procgen {
    public class SlaughterSpotGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            int attempts = 10;
            while (attempts-- > 0) {
                var posIdx = S.Sys.Areas.GenRandomSolidPos(rng);
                if (IsGoodObjectLocation(posIdx)) {
                    AddDeadBodies(posIdx, 2, SpeciesIdH.Human);
                    AddDeadBodies(posIdx, 3, SpeciesIdH.Crawler);
                    int createdMarks = 0;
                    Pos.ForEachAroundRandom(rng.Fork(), posIdx, 5, true, true, (p) => {
                        S.Sys.Dirt.Add(DirtData.Create(p,
                            Images.RandomMatching(GraphicsId.DirtFireMark01, 2),
                                Rng.URange(0.5f, 1.5f)));
                        return createdMarks++ < 3;
                    });
                    return;
                }
            }
        }

        private void AddDeadBodies(int posIdx, int min, int speciesId) {
            int maxIterations = 5;
            int addedBodies = 0;
            var subRng = rng.Fork();
            while (addedBodies < min && maxIterations-- > 0) {
                int numPeople = subRng.Range(1, 3);
                Pos.ForEachAroundRandom(subRng, posIdx, 4, true, true, (p) => {
                    return TrySpawnBody(speciesId, p, ref addedBodies, subRng, ref numPeople);
                });
            }
        }

        /// <returns>true if we want to continue</returns>
        private bool TrySpawnBody(int speciesId, int p, ref int addedBodies,
                Rng subRng, ref int numPeople) {
            if (!EntityUtils.IsEmptyFloorWithIntegrityAt(p)) { return true; }
            if (numPeople-- < 1) { return false; }
            if (S.Beings.ByPos.TryGetValue(p, out var atPos)) {
                if (Arrays.Count(atPos) > 0) {
                    return true;
                }
            }
            var data = BeingUtils.GenBeingWithInventory(
                    speciesId, BeingType.Neutral, FactionDef.Player, GenItemFlags.Casual);
            var bp = Pos.ToVector(p) + subRng.InsideUnitCircle() * 0.25f;
            var b = S.Beings.Create(bp, data);
            BeingUtils.KillHistorical(S, data, Rng.URange(Consts.TicksPerDay,
                Consts.TicksPer2Days), subRng,
                DamageUtils.CombatDamageTypes);
            data.Body.Bleed(0.5f);
            data.Body.Bleed(0.7f);
            addedBodies++;
            return true;
        }
    }
}
