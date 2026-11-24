using System.Collections.Generic;
using Game.Procgen;
using KL.Grid;
using Game.Data;
using System.Linq;
using KL.Utils;
using Game.Constants;
using Game.Systems.AI;

namespace Game.Procgen {
    // Walls in the round CPU room at the ship's center, which StartingAreaGenStage looks for.
    // The rest of the interior is RoomGrowthGenStage's.
    public class InnerWallGenStage : BaseShipGenStage {

        private readonly float centerArea;
        public InnerWallGenStage(int centerArea) {
            if (centerArea != 0) {
                // radius of x.51-x.99 ensures that the circle will be even and in the right position
                this.centerArea = centerArea + 0.6f;
            }
        }

        public override void Apply() {
            var walls = new HashSet<int>();

            if (centerArea > 0f) {
                GenerateCenterArea(walls);
            }

            GenUtils.FillWallGaps(walls);

            var reinforced = new HashSet<int>();
            if (spec.InvincibleParts.Count > 0) {
                foreach (var w in walls) {
                    if (spec.InvincibleParts.Contains(w)) {
                        reinforced.Add(w);
                    }
                }
                foreach (var w in reinforced) {
                    walls.Remove(w);
                }
            }

            BuildTiles(walls, DefIds.StructureWallsWall01);
            // nooooo, I don't think so
            BuildTiles(reinforced, DefIds.StructureWallsWall01);
        }

        private void GenerateCenterArea(HashSet<int> walls) {
            // foreach in circle will misalign the even circle by 1, this
            // should set it back to where it needs to be
            var center = Pos.FromXY(spec.CenterX, spec.CenterY);
            bool isSolid = true;
            Pos.ForEachInCircle(center, centerArea, false, (p) => {
                if (isSolid && !EntityUtils.IsEmptyFloorAt(p)) {
                    isSolid = false;
                }
            });
            if (isSolid) {
                Pos.ForEachInCircle(center, centerArea, false, (p) => {
                    spec.InvincibleParts.Add(p);
                    walls.Remove(p);
                });
                Pos.ForEachInCircle(center, centerArea, true, (p) => {
                    Pos.ToXY(p, out var px, out var py);
                    walls.Add(p);
                });
            }
        }
    }
}
