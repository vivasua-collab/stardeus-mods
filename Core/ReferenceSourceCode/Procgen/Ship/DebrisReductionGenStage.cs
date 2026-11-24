using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class DebrisReductionGenStage : BaseShipGenStage {

        private readonly float mul;

        public DebrisReductionGenStage(float outputMultiplier) {
            mul = outputMultiplier;
        }

        public override void Apply() {
            var subRng = rng.Fork();
            var objs = S.Objs.All.OrderBy(r => r.Id).ToList();
            var tempRes = new List<Obj>();
            for (int i = objs.Count - 1; i >= 0; i--) {
                var obj = objs[i];
                if (!IsWithinGenArea(obj.PosIdx)) { continue; }
                var mat = obj.Pile;
                if (mat == null) { continue; }
                var toTake = i % 2 == 0 ? Mathf.FloorToInt(mat.StackSize * mul)
                    : Mathf.CeilToInt(mat.StackSize * mul);
                mat.Take(mat.Type, null, toTake, null);
            }
        }
    }
}
