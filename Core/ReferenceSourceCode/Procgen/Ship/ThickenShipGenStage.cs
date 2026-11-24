using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using System.Linq;
using Game.Constants;
using KL.Collections;
using Game.Components;
using KL.Utils;

namespace Game.Procgen {
    [MustGoBeforeGenStage(typeof(OuterWallGenStage))]
    public class ThickenShipGenStage : BaseShipGenStage {
        private readonly HashSet<int> newFloors = new();
        public override void Apply() {
            spec.ForEachTile(FindOutline);
            BuildTiles(newFloors, DefIds.StructureFloorsFloor01);
        }

        private void FindOutline(int pos) {
            if (!EntityUtils.IsEmptyFloorWithIntegrityAt(pos)) { return; }
            // Pass to see if there are any angles
            bool canAdd = true;
            Pos.ForEachAround(pos, 1, false, true, (p) => {
                if (EntityUtils.IsObjAt(p)) {
                    // Engine?
                    canAdd = false;
                    return false;
                }
                var f = EntityUtils.FloorAt(p);
                // Angular floor
                if (f != null && !f.Integrity.CanHaveFullIntegrity) {
                    canAdd = false;
                    return false;
                }
                return true;
            });
            if (!canAdd) { return; }
            var p = Pos.N(pos);
            if (EntityUtils.IsBlankAt(p)) { newFloors.Add(p); }
            p = Pos.S(pos);
            if (EntityUtils.IsBlankAt(p)) { newFloors.Add(p); }
        }
    }
}
