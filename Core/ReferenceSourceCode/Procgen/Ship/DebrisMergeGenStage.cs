using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Data;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class DebrisMergeGenStage : BaseShipGenStage {
        public override void Apply() {
            int maxStack = 100;
            float chance = 1f;
            var subRng = rng.Fork();
            var objs = S.Objs.All.OrderBy(r => r.Id).ToList();
            var assimilated = new HashSet<int>();
            var tempRes = new List<Obj>();
            for (int i = objs.Count - 1; i >= 0; i--) {
                var obj = objs[i];
                if (!IsWithinGenArea(obj.PosIdx)) { continue; }
                if (assimilated.Contains(obj.Id)) { continue; }
                var mat = obj.Pile;
                if (mat == null) { continue; }
                if (mat.StackSize >= maxStack) { continue; }
                tempRes.Clear();
                // Assimilate nearby
                var found = S.Objs.FindVisualInRect(
                    obj.Position - new Vector2(30f, 30f),
                    obj.Position + new Vector2(30f, 30f), tempRes, null);
                if (found == 0) { continue; }
                var sortedTempRes = tempRes.OrderBy(r => subRng.Value).ToList();
                foreach (var pile in sortedTempRes) {
                    if (pile == obj) { continue; }
                    if (assimilated.Contains(pile.Id)) { continue; }
                    var c = pile.Pile;
                    if (c == null) { continue; }
                    if (c.StackSize > maxStack || c.Type != mat.Type) { continue; }
                    assimilated.Add(pile.Id);
                    if (subRng.Chance(chance)) {
                        mat.Absorb(c);
                        if (mat.StackSize > maxStack) { break; }
                    }
                }
            }
        }
    }
}
