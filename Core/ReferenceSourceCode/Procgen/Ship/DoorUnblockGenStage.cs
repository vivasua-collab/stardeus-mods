using System.Collections.Generic;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    public class DoorUnblockGenStage : BaseShipGenStage {
        public override void Apply() {
            var allDoors = S.Components.GenFind<DoorComp>();
            foreach (var door in allDoors) {
                if (!IsWithinGenArea(door.Entity.PosIdx)) { continue; }
                Pos.ForEachAround(door.Entity.PosIdx, 1, false, false,
                    RemoveObjects);
            }
        }

        private bool RemoveObjects(int pos) {
            var parent = EntityUtils.ParentObjAt(pos);
            if (parent == null) { return true; }
            if (parent.ENode?.IsShipComputer == true) {
                D.Err("Refusing to remove ship computer");
            }
            new CmdRemoveTile(parent).Execute(S);
            return true;
        }
    }
}
