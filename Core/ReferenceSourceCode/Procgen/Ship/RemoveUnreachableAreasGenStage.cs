using System.Collections.Generic;
using KL.Grid;
using System.Collections;
using KL.Utils;
using Game.Constants;
using Game.Data;
using Game.Commands;
using System.Linq;

namespace Game.Procgen {
    public class RemoveUnreachableAreasGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            if (!S.Sys.Areas.HasUnreachableAreas) { return; }
            foreach (var areaId in S.Sys.Areas.DoorCheck.UnreachableAreas) {
                RemoveArea(areaId);
            }
        }

        private void RemoveArea(int areaId) {
            // var area = S.Sys.Areas.AreaById(areaId);
            var outline = Caches.IntSet;
            outline.Clear();
            S.Sys.Areas.OutlineOfArea(areaId, outline);
            foreach (var p in outline) {
                if (IsWithinGenArea(p)) {
                    if (IsWallFacingOutdoors(p)) { continue; }
                    new CmdRemoveTile(p, WorldLayer.Walls).Execute(S);
                }
            }
        }
    }
}
