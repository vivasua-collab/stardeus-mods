using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class DirtGenStage : BaseShipGenStage {
        private Rng dirtRng;
        private readonly float multiplier;

        public DirtGenStage(float multiplier = 1f) {
            this.multiplier = multiplier;
        }
        public override void Apply() {
            dirtRng = rng.Fork();
            PosRect rekt;
            if (spec.HasGenArea) {
                rekt = spec.GenArea;
            } else {
                rekt = new PosRect(0, 0, Pos.GridW - 1, Pos.GridH - 1);
            }
            rekt.ForEach(SpawnDirt);
        }

        private void SpawnDirt(int idx) {
            if (!EntityUtils.IsEmptyFloorAt(idx)) { return; }
            var rng = dirtRng.Fork();
            if (multiplier < 1f && !rng.Chance(multiplier)) { return; }

            var pos = EntityUtils.CenterOf(idx) + rng.InsideUnitCircle();
            var n = rng.GetSimplex(pos.x, pos.y, 1f);
            var n2 = rng.GetSimplex(pos.x, pos.y, 7.3f);
            var n3 = rng.GetSimplex(pos.y, pos.x, 0.71f);
            var n4 = rng.GetSimplex(-pos.y, -pos.x, 2.3f);

            var dirtSprite = PickType(n3);


            //// D.Log("N at {0} {1} {2}", pos, n, n2);
            if (rng.Chance(System.Math.Abs(n + n2))) {
                if (rng.Chance(0.3f + System.Math.Abs(n4))) {
                    S.Sys.Dirt.Add(DirtData.Create(idx,
                        Images.RandomMatching(dirtSprite, 2, rng),
                        rng.Range(0.5f, 1.5f)));
                }
            }
        }

        private readonly string[] types = new[] {
            GraphicsId.DirtBloodSplash01,
            GraphicsId.DirtDirtMark01,
            GraphicsId.DirtDirtMark01,
            GraphicsId.DirtOilSplash01,
            GraphicsId.DirtFireMark01,
            GraphicsId.DirtFireMark01,
            GraphicsId.DirtDirtSoil01,
        };

        private string PickType(float n) {
            if (n > 0.5f) {
                n = 0.5f;
            } else if (n < -0.5f) {
                n = -0.5f;
            }
            var type = Mathf.RoundToInt((n + 0.5f) * (types.Length - 1));
            return types[type];
        }
    }
}
