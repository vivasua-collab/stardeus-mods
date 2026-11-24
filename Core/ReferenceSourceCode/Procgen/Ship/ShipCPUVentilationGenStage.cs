using KL.Grid;
using Game.Data;
using Game.Constants;
using Game.Commands;
using KL.Utils;
using Game.Components;

namespace Game.Procgen {
    public class ShipCPUVentilationGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                c => IsWithinGenArea(c.Tile.Transform.PosIdx));
            if (cpu == null) {
                D.Err("No CPU");
                return;
            }
            int dealt = 0;
            var cpuPos = cpu.Tile.Transform.PosIdx;
            var area = S.Sys.Areas.AnyAreaAt(cpuPos);
            Pos.ForEachInOuterRadius(cpuPos, 3, (p) => {
                if (dealt > 3) { return; }
                if (EntityUtils.IsEmptyFloorWithIntegrityAt(p)) {
                    if (area.Contains(p)) {
                        spec.InvincibleParts.Remove(p);
                        new CmdDealDamage(p, WorldLayer.Floor, 90, DamageType.Structural).Execute(S);
                        dealt++;
                    }
                }
            });
            if (dealt == 0) {
                D.Err("Could not open up any floor near ship computer");
            }
        }
    }
}
