using System.Collections.Generic;
using Game.Data;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Procgen {
    public class DebrisCleanupGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            var cpuPos = spec.CPUPos;
            var objs = S.Objs.All;
            var r = rng.Fork();
            for (int i = objs.Count - 1; i >= 0; i--) {
                var obj = objs[i];
                // Not a debris
                if (obj.Definition.MatType == null) { continue; }
                if (!IsWithinGenArea(obj.PosIdx)) { continue; }
                // A pile that is already hauling-forbidden was put there on purpose (stasis
                // pod rations, survivor area food and fuel) and must stay where the player
                // is told to find it. Clearing the CPU's surroundings is for random debris,
                // and scattered the pod's rations whenever the array was generated close to
                // the CPU (STA-1608).
                var isPlaced = obj.Pile?.IsForbidden == true;
                if (!isPlaced && IsClearedAroundCPU(obj.PosIdx, cpuPos, r)) {
                    Relocate(obj, r);
                    continue;
                }
                D.Ass(obj.PosIdx == EntityUtils.ToPos(obj.Position),
                    "Obj pos != position: {0} != {1}",
                        Pos.String(obj.PosIdx), obj.Position);
                if (!EntityUtils.IsEmptyFloorAt(obj.PosIdx, true)) {
                    // Relocate rather than destroy: material piles here were already
                    // counted by the starting resources top-up stage
                    if (!Relocate(obj, r)) {
                        S.Objs.Destroy(obj);
                    }
                }
            }
        }

        private static bool IsClearedAroundCPU(int pos, int cpuPos, Rng rng) {
            var sqrDist = Pos.SqrDistance(pos, cpuPos);
            if (sqrDist < 5 * 5) { return true; }
            if (sqrDist < 10 * 10) { return rng.Chance(0.7f); }
            if (sqrDist < 15 * 15) { return rng.Chance(0.5f); }
            return false;
        }

        private bool Relocate(Obj obj, Rng rng) {
            int attempts = 50;
            while (attempts-- > 0) {
                var pos = S.Sys.Areas.GenRandomSolidPos(rng);
                if (EntityUtils.IsEmptyFloorAt(pos)) {
                    var newPos = EntityUtils.CenterOf(pos);
                    obj.Transform.TeleportTo(newPos);
                    obj.Graphics.TeleportTo(newPos);
                    return true;
                }
            }
            return false;
        }
    }
}
