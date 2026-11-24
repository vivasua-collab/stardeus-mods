using KL.Grid;
using Game.Data;
using System.Linq;
using KL.Utils;
using Game.Constants;
using System.Collections.Generic;
using Game.Components;
using Game.Commands;
using KL.Randomness;
using Game.CodeGen;
using System.Collections;

namespace Game.Procgen {
    public class LandingAreaGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        private readonly int width;
        private readonly int height;
        public LandingAreaGenStage(int width = 6, int height = 3) {
            this.width = width;
            this.height = height;
        }

        public override void Apply() {
            if (S.Sys.Areas.LandableAreaSize >= width * height) {
                // we already have enough
                return;
            }
            GenerateArea();
        }

        private void GenerateArea() {
            var shipBounds = spec.Bounds;
            int startX = rng.Range(shipBounds.MinX + 20, shipBounds.MaxX - 20);
            var startY = shipBounds.MinY - 2;
            var curX = startX;
            var curY = startY;
            var maxY = S.GridHeight / 2;
            while (curX < shipBounds.MaxX) {
                if (TryBuildAttachmentAtX(ref curX, ref curY, maxY, startY, +1, out var triedPos)) {
                    return;
                }
            }
            curX = startX;
            curY = startY;
            while (curX > shipBounds.MinX) {
                if (TryBuildAttachmentAtX(ref curX, ref curY, maxY, startY, -1, out var triedPos)) {
                    return;
                }
            }
            StartOver = true;
        }

        private bool TryBuildAttachmentAtX(ref int curX, ref int curY, int maxY,
                int startY, int dir, out int triedPos) {
            for (int i = 0; i < width; i++) {
                var floor = EntityUtils.FloorAt(Pos.FromXY(curX + i, curY));
                if (floor == null) {
                    curY++;
                    if (curY >= maxY) {
                        curY = startY;
                        curX += dir;
                    }
                    triedPos = -1;
                    return false;
                }
                D.Warn("Found floor: {0}", floor);
                if (!floor.Integrity.CanHaveFullIntegrity) {
                    // angled floor, nope
                    curY = startY;
                    curX += dir;
                    triedPos = -1;
                    return false;
                }
            }
            // floor is not null, we want to try to build

            // try to build
            // we are on the floor now, attachment is 3x3, so we need to move
            // down by 3 tiles
            int pos = Pos.FromXY(curX, curY - height);
            triedPos = pos;
            D.Warn("Trying to build attachment at {0}", Pos.String(triedPos));
            if (!TryBuildAttachmentAt(pos)) {
                curY = startY;
                curX += dir;
                return false;
            }
            return true;
        }

        private bool TryBuildAttachmentAt(int pos) {
            // check if we can build WxH in space starting from here
            var rekt = new PosRect(pos, Pos.NeighborAt(pos, width - 1, height - 1));
            bool isEmpty = true;
            rekt.ForEach((p) => {
                if (isEmpty && !EntityUtils.IsEmptySpaceAt(p)) {
                    isEmpty = false;
                }
            });
            if (!isEmpty) {
                return false;
            }
            rekt.ForEach((p) => {
                var fl = EntityUtils.CreatePrototype<Tile>(
                    DefIds.StructureFloorsFloorReinforced03);
                BuildTile(p, fl);
            });
            return true;
        }
    }
}
