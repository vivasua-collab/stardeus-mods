using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using Game.Constants;
using KL.Utils;
using System;

namespace Game.Procgen {
    public class OuterWallGenStage : BaseShipGenStage {
        private readonly Func<ShipSpec, bool> makeDouble;

        public OuterWallGenStage(Func<ShipSpec, bool> makeDouble) {
            this.makeDouble = makeDouble;
        }

        public override void Apply() {
            var walls = new HashSet<int>();

            spec.ForEachTile((p) => {
                if (!EntityUtils.IsSurroundedBySolidFloor(p)) {
                    walls.Add(p);
                }
            }, 1);

            GenUtils.FillWallGaps(walls);

            var doubleWals = makeDouble != null && makeDouble.Invoke(spec);
            // D.Warn("Building outer walls for ship with size: {0}x{1}. Double: {2}",
            //     spec.Width, spec.Height, doubleWals);

            if (doubleWals) {
                BuildWalls(MakeDouble(walls), DefIds.StructureWallsWall01);
            } else {
                // BuildTiles(walls, DefIds.StructureWallsWallReinforced01);
                BuildWalls(walls, DefIds.StructureWallsWall01);
            }
            RecordHullWalls(walls);
        }

        // Only the outer layer: the inner one of a double wall never faces space
        private void RecordHullWalls(HashSet<int> walls) {
            if (isPreview) { return; }
            foreach (var pos in walls) {
                var wall = EntityUtils.WallAt(pos);
                if (wall != null) {
                    spec.HullWalls.Add(wall);
                }
            }
        }

        private void BuildWalls(IEnumerable<int> list, string def) {
            var failed = new HashSet<int>();
            foreach (var pos in list) {
                if (pos < 1) {
                    D.Err("Trying to build tile at invalid pos (from a list): {0} {1}", pos, def);
                    return;
                }
                if (isPreview) {
                    Pos.ToXY(pos, out var x, out var y);
                    previewTex.SetPixel(x, y, Consts.ColorWhite);
                } else {
                    // D.Warn("Building tile at {0}: {1}", Pos.String(pos), def);
                    var tile = BuildTile(pos, EntityUtils.CreatePrototype<Tile>(def), allowFailure: true);
                    if (tile == null) {
                        failed.Add(pos);
                    }
                }
            }
            foreach (var f in failed) {
                Pos.ForEachAround(f, 1, false, true, (p) => {
                    BuildTile(p, EntityUtils.CreatePrototype<Tile>(def), allowFailure: true);
                    return true;
                });
            }
        }

        private void TryAddNewWallAt(int s, int dx, int dy) {
            if (!EntityUtils.IsEmptyFloorAt(Pos.NeighborAt(s, dx * 1, dy * 1))) { return; }
            if (!EntityUtils.IsEmptyFloorAt(Pos.NeighborAt(s, dx * 2, dy * 2))) { return; }
            if (!EntityUtils.IsEmptyFloorAt(Pos.NeighborAt(s, dx * 3, dy * 3))) { return; }
            if (!EntityUtils.IsEmptyFloorAt(Pos.NeighborAt(s, dx * 4, dy * 4))) { return; }
            newWalls.Add(s);
        }

        private readonly List<int> newWalls = new();

        private List<int> MakeDouble(HashSet<int> walls) {
            newWalls.Clear();
            foreach (var wall in walls) {
                // 1. find the opposite direction of space
                var n = Pos.N(wall);
                var s = Pos.S(wall);
                var e = Pos.E(wall);
                var w = Pos.W(wall);
                if (!EntityUtils.IsFloorAt(n) && EntityUtils.IsFloorAt(s)) {
                    TryAddNewWallAt(s, 0, -1);
                }
                if (!EntityUtils.IsFloorAt(s) && EntityUtils.IsFloorAt(n)) {
                    TryAddNewWallAt(n, 0, 1);
                }
                if (!EntityUtils.IsFloorAt(e) && EntityUtils.IsFloorAt(w)) {
                    TryAddNewWallAt(w, -1, 0);
                }
                if (!EntityUtils.IsFloorAt(w) && EntityUtils.IsFloorAt(e)) {
                    TryAddNewWallAt(e, 1, 0);
                }
            }
            newWalls.AddRange(walls);
            return newWalls;
        }
    }
}
