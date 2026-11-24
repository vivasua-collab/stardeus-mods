using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class DebrisShuffleGenStage : BaseShipGenStage {
        //public override bool IsAsync => true;

        public override void Apply() {
            var r = rng.Fork();
            var objs = S.Objs.All;
            for (int i = objs.Count - 1; i >= 0; i--) {
                var obj = objs[i];
                if (!IsWithinGenArea(obj.PosIdx)) { continue; }
                if (!EntityUtils.IsFloorAt(obj.PosIdx)) {
                    var posBefore = obj.Transform.Position;
                    var newPos = posBefore
                        + r.InsideUnitCircle().normalized * r.Range(1f, 2f);
                    if (EntityUtils.IsWithinBounds(newPos)) {
                        obj.Transform.TeleportTo(newPos);
                        obj.Graphics.TeleportTo(newPos);
                    }
                }
            }
        }
    }
}
