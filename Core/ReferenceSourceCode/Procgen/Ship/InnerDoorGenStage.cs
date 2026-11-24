using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;

namespace Game.Procgen {
    public class InnerDoorGenStage : BaseShipGenStage {
        // string is a key of an area pair
        protected readonly Dictionary<string, HashSet<int>> edges = new();

        private readonly HashSet<int> processed = new();

        protected int createdDoors;

        // Ordered by priority

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
            createdDoors = 0;
            edges.Clear();
            //D.Err("Creating doors, area count: {0}", S.Sys.Areas.Areas.Count);
            var areas = S.Sys.Areas.Areas.OrderBy(a => a.Value.Count).ToArray();
            foreach (var area in areas) {
                CreateDoors(area.Key, area.Value);
            }

            #if DEBUG_GEN
            // D.Warn("{0} created {1} doors", LogPrefix, createdDoors);
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

        protected void CreateDoors(int areaId, HashSet<int> area) {
            if (!IsWithinGenArea(area.First())) { return; }
            // center isle
            if (spec.InvincibleParts.Contains(area.First())) { return; }
            currentArea = area;
            var outline = new HashSet<int>();
            S.Sys.Areas.OutlineOfArea(areaId, outline);
            var sortedOutline = outline.ToList();
            sortedOutline.Sort();
            foreach (var wallTile in sortedOutline) {
                // fill out edges. We never place a door in a diagonal spot,
                // so let's just find all non-diagonal neighbors
                Pos.ForEachAround(wallTile, 1, false, false, neighbor => {
                    if (currentArea.Contains(neighbor)) { return true; }
                    if (sortedOutline.Contains(neighbor)) { return true; }
                    // If it's not in our area, it may be adjacent to some other
                    // area, or it may be the outside
                    var parentAreaId = S.Sys.Areas.At(neighbor);
                    if (parentAreaId == 0) { return true; }
                    var key = AreaPairKey(areaId, parentAreaId);
                    var edge = edges.GetInit(key);
                    edge.Add(wallTile);
                    return true;
                });
            }

            var sortedEdges = edges.OrderBy(e => e.Key);

            // Let's create a door on each of those edges
            foreach (var edge in sortedEdges) {
                if (CreateDoor(edge.Value)) { createdDoors++; }
            }
        }

        protected bool CreateDoor(HashSet<int> wall) {
            good.Clear();
            ends.Clear();
            angleNeighbors.Clear();
            angles.Clear();

            var sortedWall = wall.ToList();
            sortedWall.Sort();

            foreach (var pos in sortedWall) {
                if (!processed.Add(pos)) {
                    // We already processed this particular edge
                    return false;
                }

                bool isAngle = IsWallAngle(pos);

                if (isAngle) { angles.Add(pos); }
            }

            foreach (var pos in sortedWall) {
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

            int doorIdx = -1;
            var subRng = new Rng(Hashes.Quick(spec.Seed, wall.Sum()));

            if (good.Count > 0) {
                doorIdx = subRng.From(good);
            } else if (ends.Count > 0) {
                doorIdx = subRng.From(ends);
            } else if (angleNeighbors.Count > 0) {
                doorIdx = subRng.From(angleNeighbors);
            }

            if (doorIdx == -1) { return false; }

            BuildDoorAt(doorIdx);

            return true;
        }

        // False when the door could not be placed
        protected bool BuildDoorAt(int doorIdx) {
            var doorPrototype = EntityUtils.CreatePrototype<Tile>(
                DefIds.StructureDoorsDoor01);

            var wallAt = BuildTile(doorIdx, doorPrototype);
            if (wallAt == null || wallAt.Door == null) { return false; }
            wallAt.Transform.AutoUpdateRotation();
            wallAt.Door.SetClosed(true);
            return true;
        }

        public static bool IsWallAngle(int pos) {
            var wallNS = EntityUtils.IsWallAt(Pos.N(pos))
                || EntityUtils.IsWallAt(Pos.S(pos));

            var wallEW = EntityUtils.IsWallAt(Pos.E(pos))
                || EntityUtils.IsWallAt(Pos.W(pos));

            var isAngle = wallNS && wallEW;
            return isAngle;
        }
    }
}
