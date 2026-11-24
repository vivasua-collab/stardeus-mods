using System.Collections.Generic;
using KL.Grid;
using System.Collections;
using KL.Utils;
using Game.Constants;
using Game.Data;
using Game.Commands;
using System.Linq;
using System;

namespace Game.Procgen {
    [MustGoAfterGenStage(typeof(InnerWallGenStage))]
    public class RemoveSmallAreasGenStage : BaseShipGenStage {
        public override bool RequiresAreas => true;
        public override void Apply() {
            var minAreaSize = Math.Min(
                (spec.CellWidth - 1) * (spec.CellHeight - 1), 25);
            foreach (var a in S.Sys.Areas.Areas) {
                var pos = a.Value.First();
                if (!IsWithinGenArea(pos)) { continue; }
                if (a.Value.Count > 10 && spec.InvincibleParts.Contains(pos)) {
                    continue;
                }
                if (a.Value.Count < minAreaSize) {
                    RemoveArea(a.Key, a.Value);
                }
            }
        }

        private void RemoveWallsFinal(int areaId) {
            var outline = Caches.IntSet;
            outline.Clear();
            S.Sys.Areas.OutlineOfArea(areaId, outline);
            foreach (var p in outline) {
                if (IsWallFacingOutdoors(p)) {
                    continue;
                }
                new CmdRemoveTile(p, WorldLayer.Walls).Execute(S);
            }
        }

        private void RemoveArea(int areaId, HashSet<int> area) {
            var pos = area.First();
            if (!IsWithinGenArea(pos)) { return; }
            // nothing could be inside an area that small that should be protected
            if (area.Count > 10 && spec.InvincibleParts.Contains(pos)) {
                return;
            }
            // var rect = Pos.Encapsulate(area);
            // if (rect.Width < 3 || rect.Height < 3) {
            //     // small useless area
            //     foreach (var p in area) {
            //         new CmdRemoveTile(p, WorldLayer.Floor).Execute(S);
            //     }
            //     return;
            // }
            int areaAtPos = S.Sys.Areas.At(pos);
            var removedAWall = false;
            if (!removedAWall) {
                removedAWall = BreakWallIfAreaOnOtherSide(areaAtPos, pos, 0, 1);
            }
            if (!removedAWall) {
                removedAWall = BreakWallIfAreaOnOtherSide(areaAtPos, pos, 1, 0);
            }
            if (!removedAWall) {
                removedAWall = BreakWallIfAreaOnOtherSide(areaAtPos, pos, 0, -1);
            }
            if (!removedAWall) {
                removedAWall = BreakWallIfAreaOnOtherSide(areaAtPos, pos, -1, 0);
            }
            if (!removedAWall) {
                RemoveWallsFinal(areaId);
            }
        }

        private bool BreakWallIfAreaOnOtherSide(int areaAtPos, int pos, int x, int y) {
            if (areaAtPos == 0) { return false; }
            int areaAtN = areaAtPos;
            while (areaAtN == areaAtPos) {
                pos = Pos.NeighborAt(pos, x, y);
                if (EntityUtils.IsWallAt(pos)) { continue; }
                areaAtN = S.Sys.Areas.At(pos);
            }
            if (areaAtN != 0) {
                var areaAtNOutline = new HashSet<int>();
                S.Sys.Areas.OutlineOfArea(areaAtN, areaAtNOutline);
                var areaAtOutline = new HashSet<int>();
                S.Sys.Areas.OutlineOfArea(areaAtPos, areaAtOutline);
                var intersection = areaAtOutline.Intersect(areaAtNOutline);
                foreach (var p in intersection) {
                    RemoveWallIfBetweenAreas(p, areaAtPos, areaAtN);
                }
                return true;
            }
            return false;
        }

        private void RemoveWallIfBetweenAreas(int p, int a1, int a2) {
            bool needRemoval = false;
            var a = S.Sys.Areas;
            if (a.At(Pos.N(p)) == a1 && a.At(Pos.S(p)) == a2) {
                needRemoval = true;
                // opposites
            } else if (a.At(Pos.E(p)) == a1 && a.At(Pos.W(p)) == a2) {
                needRemoval = true;
            } else if (a.At(Pos.S(p)) == a1 && a.At(Pos.N(p)) == a2) {
                needRemoval = true;
            } else if (a.At(Pos.W(p)) == a1 && a.At(Pos.E(p)) == a2) {
                needRemoval = true;
            } else if (a.At(Pos.N(p)) == a1 && a.At(Pos.S(p)) == a2) {
                needRemoval = true;
                // 90 degree angles
            } else if (a.At(Pos.W(p)) == a2 && a.At(Pos.N(p)) == a2) {
                needRemoval = true;
            } else if (a.At(Pos.N(p)) == a2 && a.At(Pos.E(p)) == a2) {
                needRemoval = true;
            } else if (a.At(Pos.E(p)) == a2 && a.At(Pos.S(p)) == a2) {
                needRemoval = true;
            } else if (a.At(Pos.S(p)) == a2 && a.At(Pos.W(p)) == a2) {
                needRemoval = true;
            }
            if (needRemoval) {
                new CmdRemoveTile(p, WorldLayer.Walls).Execute(S);
            }
        }
    }
}
