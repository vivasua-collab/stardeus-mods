using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Utils;

// 803254353
namespace Game.Procgen {
    [MustGoAfterGenStage(
        // typeof(AirlockGenStage),
        typeof(InnerDoorGenStage),
        typeof(RemoveSmallAreasGenStage))]
    public class MissingDoorGenStage : InnerDoorGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            createdDoors = 0;
            edges.Clear();
            foreach (var area in S.Sys.Areas.DoorCheck.UnreachableAreas) {
                CreateDoors(area, S.Sys.Areas.Areas[area]);
            }

            if (spec.InvincibleParts.Count <= 0) { return; }

            // check center area
            var centerWalls = new HashSet<Tile>();
            foreach (var p in spec.InvincibleParts) {
                var wall = EntityUtils.WallAt(p);
                if (wall == null) { continue; }
                // if (wall.TryGetComponent<DoorComp>(out var door)) {
                //     // we have a door, nothing to do
                //     return;
                // }
                centerWalls.Add(wall);
            }


            foreach (var w in centerWalls) {
                var pos = w.PosIdx;
                Pos.ToXY(pos, out var wx, out var wy);
                if (wy == spec.CenterY) {
                    TryAddDoorAt(pos, Pos.N(pos), pos, Pos.S(pos), Pos.S(Pos.S(pos)));
                } else if (wx == spec.CenterX) {
                    TryAddDoorAt(pos, Pos.E(pos), Pos.E(Pos.E(pos)), pos, Pos.W(pos));
                }
            }
        }
        private void TryAddDoorAt(int pos, params int[] wallPositions) {
            foreach (var wp in wallPositions) {
                // already have a door
                if (GenUtils.IsDoor(wp)) {
                    return;
                }
            }
            if (!IsWallAngle(pos)) {
                BuildDoorAt(pos);
            } else {
                // build a door in the vertical area around center
                CreateDoor(new HashSet<int>(wallPositions));
            }
        }
    }
}
