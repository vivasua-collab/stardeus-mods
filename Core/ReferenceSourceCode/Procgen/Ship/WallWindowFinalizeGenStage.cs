using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    public class WallWindowFinalizeGenStage : BaseShipGenStage {

        // Ordered by priority
        public override void Apply() {
            var gfDef = The.Defs.RandomMatching(DefIds.StructureFloorsFloorGlass01,
                spec.CosmeticRng);

            foreach (Tile tile in S.DataGrids[WorldLayer.Walls].Data) {
                if (tile == null) { continue; }
                if (!IsWithinGenArea(tile.PosIdx)) { continue; }
                var curFloor = EntityUtils.FloorAt(tile.PosIdx);
                if (curFloor == null) {
                    D.ErrSoft("Floor is null underneath a wall at! {0}",
                        Pos.String(tile.PosIdx));
                    continue;
                }
                if (curFloor.DefinitionId.Contains("Glass")) {
                    D.Err("Floor is already glass underneath a wall! {0} | floor: {1} | under: {2}",
                        Pos.String(tile.PosIdx), curFloor, tile);
                    continue;
                }
                if (!tile.DefinitionId.Contains("/Walls/Window")) {
                    continue;
                }

                var gFloor = EntityUtils.CreatePrototype<Tile>(gfDef.Id);
                // gen replace won't work anymore because glass floors have different comp lengths
                // curFloor.GenReplaceWith(S, gFloor);
                BuildTile(tile.PosIdx, gFloor);
                // D.Warn("Replaced glass floor at {0}", Pos.String(curFloor.PosIdx));
            }
        }
    }
}
