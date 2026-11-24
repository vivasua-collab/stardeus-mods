using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    public class WallWindowGenStage : BaseShipGenStage {
        // string is a key of an area pair
        private readonly Dictionary<string, HashSet<int>> edges = new();

        private readonly HashSet<int> processed = new();

        private int createdWindows;

        // Ordered by priority

        private readonly HashSet<int> bad = new();
        // good tile is one that has neighbors on north+south XOR east+west
        // neighbor should not be an angle
        private readonly HashSet<int> good = new();
        // tile that has only one neighbor that is not an angle
        private readonly HashSet<int> ends = new();
        // same as good, but any neighbor is an angle
        private readonly HashSet<int> angleNeighbors = new();
        // a tile with neighbors on (south | north) + (east | west)
        private readonly HashSet<int> angles = new();
        public override bool RequiresAreas => true;

        private HashSet<int> currentArea;
        public override void Apply() {
            createdWindows = 0;
            edges.Clear();
            var areas = S.Sys.Areas.Areas.OrderBy(a => spec.CosmeticRng.Value).ToArray();
            foreach (var area in areas) {
                if (!IsWithinGenArea(area.Value.First())) { continue; }
                CreateWindows(area.Key, area.Value);
            }

            #if DEBUG_GEN
            // D.Warn("{0} created {1} doors", LogPrefix, createdVents);
            #endif
        }

        private string AreaPairKey(int a1, int a2) {
            var lowerId = a1;
            var higherId = a2;
            if (a1 > a2) {
                higherId = lowerId;
                lowerId = a2;
            }
            return string.Format("{0}-{1}", lowerId, higherId);
        }

        private void CreateWindows(int areaId, HashSet<int> area) {
            currentArea = area;
            var outline = new HashSet<int>();
            S.Sys.Areas.OutlineOfArea(areaId, outline);
            var sortedOutline = outline.ToList();
            sortedOutline.Sort();
            foreach (var wallTile in sortedOutline) {
                // fill out edges. We never place a door in a diagonal spot,
                // so let's just find all non-diagonal neighbors
                Pos.ForEachAround(wallTile, 1, false, false, neighbor => {
                    if (sortedOutline.Contains(neighbor)) { return true; }
                    if (currentArea.Contains(neighbor)) { return true; }
                    // If it's not in our area, it may be adjacent to some other
                    // area, or it may be the outside
                    var parentAreaId = S.Sys.Areas.At(neighbor);
                    var key = AreaPairKey(areaId, parentAreaId);
                    var edge = edges.GetInit(key);
                    edge.Add(wallTile);
                    return true;
                });
            }

            // Let's create a door on each of those edges
            foreach (var edge in edges) {
                if (CreateWindows(edge.Value)) { createdWindows++; }
            }
        }

        private bool CreateWindows(HashSet<int> wall) {
            var gridHeight = S.GridHeight;

            bad.Clear();
            good.Clear();
            ends.Clear();
            angleNeighbors.Clear();
            angles.Clear();

            var sortedWall = wall.ToList();
            sortedWall.Sort();

            foreach (var pos in sortedWall) {
                if (GenUtils.IsDoor(pos)) {
                    Pos.ForEachAround(pos, 1, true, false,
                        (doorPos) => bad.Add(doorPos));
                }

                if (!processed.Add(pos)) {
                    // We already processed this particular edge
                    return false;
                }

                var wallNS = EntityUtils.IsWallAt(Pos.N(pos))
                    || EntityUtils.IsWallAt(Pos.S(pos));

                var wallEW = EntityUtils.IsWallAt(Pos.E(pos))
                    || EntityUtils.IsWallAt(Pos.W(pos));

                if (wallNS && wallEW) { angles.Add(pos); }
            }

            foreach (var pos in sortedWall) {
                if (bad.Contains(pos)) { continue; }
                if (angles.Contains(pos)) { continue; }
                var nIdx = Pos.N(pos);
                var sIdx = Pos.S(pos);
                var eIdx = Pos.E(pos);
                var wIdx = Pos.W(pos);
                bool isAngleNeighbor = angles.ContainsAny(nIdx, sIdx, eIdx, wIdx);
                bool isInLine = wall.ContainsAll(nIdx, sIdx) ||
                        wall.ContainsAll(eIdx, wIdx);
                int neighborCount = wall.ContainsCount(
                        nIdx, eIdx, sIdx, wIdx);

                if (!isAngleNeighbor && isInLine) {
                    good.Add(pos);
                } else if (neighborCount == 1) {
                    ends.Add(pos);
                } else {
                    angleNeighbors.Add(pos);
                }
            }

            int skipEvery = spec.CosmeticRng.Range(1, 5);
            int iter = 0;

            foreach (var windIdx in good) {
                if (skipEvery > 1) {
                    if (iter++ % skipEvery == 0) {
                        continue;
                    }
                }
                var oldWall = EntityUtils.WallAt(windIdx);
                //don't replace electric stuff with windows
                if (oldWall.ENode != null) { continue; }
                string winDefId = DefIds.StructureWallsWindow01;
                if (oldWall.Definition.Id.Contains("Walls/Wall")) {
                    winDefId = oldWall.Definition.Id.Replace("Walls/Wall",
                        "Walls/Window");
                    if (The.Defs.TryGet(winDefId) == null)  {
                        winDefId = DefIds.StructureWallsWindow01;
                    }
                }
                var winProto = EntityUtils.CreatePrototype<Tile>(winDefId);

                // Rotate the door so it has correct alignment with the walls
                var dnIdx = Pos.N(windIdx);
                var dsIdx = Pos.S(windIdx);
                if (EntityUtils.IsWallAt(dnIdx) && EntityUtils.IsWallAt(dsIdx)) {
                    winProto.Transform.Rotation = Facing.Type.Right;
                } else {
                    winProto.Transform.Rotation = Facing.Type.Down;
                }

                BuildTile(windIdx, winProto);
            }

            return true;
        }
    }
}
