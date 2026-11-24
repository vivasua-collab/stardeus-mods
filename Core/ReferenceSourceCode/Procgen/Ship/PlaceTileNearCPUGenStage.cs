using KL.Grid;
using Game.Data;
using Game.Constants;
using KL.Utils;
using Game.Commands;

namespace Game.Procgen {
    public class PlaceTileNearCPUGenStage : BaseShipGenStage {
        private readonly int minDist;
        private readonly int maxDist;
        private readonly string defId;
        public PlaceTileNearCPUGenStage(int minDistFromCPU, int maxDistFromCPU, string defId) {
            this.defId = defId;
            this.minDist = minDistFromCPU;
            this.maxDist = maxDistFromCPU;
        }
        public override void Apply() {
            var shipCpuPos = spec.CPUPos;
            D.Ass(shipCpuPos > 0, "No cpu pos!");

            var proto = EntityUtils.CreatePrototype<Tile>(defId);

            Pos.ForEachAroundRandom(rng, shipCpuPos, maxDist, false, true, (p) => {
                if (Pos.Distance(shipCpuPos, p) < minDist) {
                    // too close to CPU
                    return true;
                }
                if (CmdPlaceTile.CanPlace(p, proto, WorldLayer.Objects, warn: false)) {
                    BuildTile(p, proto);
                    return false;
                }
                return true;
            });
        }
    }
}
