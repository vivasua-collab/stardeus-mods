#define DEBUG_GEN
using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using Game.Constants;
using KL.Collections;
using Game.Commands;
using KL.Utils;

namespace Game.Procgen {
    public class PassageUnblockGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            var rect = spec.Bounds;

            int wallStart = -1;
            // Scan lines and check if there are objects fully packed in between
            // any two walls
            for (int x = rect.MinX; x < rect.MaxX; x++) {
                wallStart = -1;
                for (int y = rect.MinY; y < rect.MaxY; y++) {
                    var pos = Pos.FromXY(x, y);
                    // It's open space, we don't care
                    if (!EntityUtils.IsFloorAt(pos)) {
                        wallStart = -1;
                        continue;
                    }
                    // Check gaps and reset wallStart to -1 if there is no object
                    if (!EntityUtils.IsObjAt(pos) && wallStart != -1) {
                        wallStart = -1;
                        continue;
                    }
                    if (EntityUtils.IsWallAt(pos)) {
                        // Is this a start of a room? First wall?
                        if (wallStart == -1) {
                            wallStart = y;
                            continue;
                        }
                        // We already had a wall somewhere and there were no gaps
                        RemoveTile(Pos.FromXY(x, y - 1));
                        // D.Warn("GEN_SHIP {0} Unblocking passage UP: {1}",
                        //    LogPrefix, pos);
                    }
                }
            }

            for (int y = rect.MinY; y < rect.MaxY; y++) {
                wallStart = -1;
                for (int x = rect.MinX; x < rect.MaxX; x++) {
                    var pos = Pos.FromXY(x, y);
                    // It's open space, we don't care
                    if (!EntityUtils.IsFloorAt(pos)) {
                        wallStart = -1;
                        continue;
                    }
                    // Check gaps and reset wallStart to -1 if there is no object
                    if (!EntityUtils.IsObjAt(pos) && wallStart != -1) {
                        wallStart = -1;
                        continue;
                    }
                    if (EntityUtils.IsWallAt(pos)) {
                        // Is this a start of a room? First wall?
                        if (wallStart == -1) {
                            wallStart = y;
                            continue;
                        }
                        // We already had a wall somewhere and there were no gaps
                        // D.Warn("GEN_SHIP {0} Unblocking passage LEFT: {1}",
                        //    LogPrefix, pos);
                        RemoveTile(Pos.FromXY(x - 1, y));
                    }
                }
            }
        }

        private void RemoveTile(int at) {
            var objAt = EntityUtils.ObjAt(at);
            if (objAt != null && objAt.ENode != null
                    && objAt.ENode.IsShipComputer) {
                D.Err("Refusing to remove ship computer!");
            } else {
                new CmdRemoveTile(objAt).Execute(S);
            }
        }
    }
}
