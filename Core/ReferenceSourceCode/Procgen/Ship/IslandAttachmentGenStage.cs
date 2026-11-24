#if OBSOLETE
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Procgen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public class IslandAttachmentGenStage : BaseShipGenStage {
        protected struct ShipIslandPair {
            public int IdxA;
            public int IdxB;
            public ShipIsland IslandA;
            public ShipIsland IslandB;
            // Cells, don't refactor to int!
            public int ClosestA;
            public int ClosestB;
            public float Distance;
        }

        private readonly List<string> covered = new List<string>();
        private readonly List<ShipIslandPair> pairs = new List<ShipIslandPair>();
        private readonly List<int> connectionCells = new List<int>();
        private readonly HashSet<int> connectedIslandIndices = new HashSet<int>();

        public override void Apply() {
            spec = AttachIslands(spec);
            var newTiles = new List<int>();
            foreach (var con in connectionCells) {
                Pos.ToXY(con, out var x, out var y);
                AppendAdjustedPositionMirrored(x, y, newTiles);
            }

            BuildTiles(newTiles, DefIds.StructureFloorsFloor02);
        }

        public ShipSpec AttachIslands(ShipSpec spec) {
            // 0. To find the closest two points between two islands
            // We will find AABB of each island and brute force check
            // closest row and a column
            // 1. Find distances between each island pair
            // 2. Connect each island with it's closest neighbor
            //    2.1 Run A* to find a path for the connection
            covered.Clear();
            connectionCells.Clear();
            connectedIslandIndices.Clear();

            for (int i = 0; i < spec.Islands.Count; i++) {
                var islandA = spec.Islands[i];
                for (int j = 0; j < spec.Islands.Count; j++) {
                    if (i == j) { continue; }
                    // will not pair the same island
                    if (IsCovered(i, j)) { continue; }
                    var islandB = spec.Islands[j];
                    FindClosestCells(islandA, islandB,
                        out var closestA, out var closestB, out var distance);
                    pairs.Add(new ShipIslandPair {
                        IdxA = i,
                        IdxB = j,
                        IslandA = islandA,
                        IslandB = islandB,
                        ClosestA = closestA,
                        ClosestB = closestB,
                        Distance = distance,
                    });
                }
            }

            // Create actual connections between islands
            var pendingList = pairs.OrderBy(p => p.Distance);
            FastConnectPairs(spec, pendingList);

            return spec;
        }

        // Fast implies that it connects islands optimistically
        // It may end up with two or more island groups
        private void FastConnectPairs(ShipSpec spec,
                IOrderedEnumerable<ShipIslandPair> pendingList) {
            foreach (var pair in pendingList) {
                if (connectedIslandIndices.Contains(pair.IdxA) &&
                        connectedIslandIndices.Contains(pair.IdxB)) {
                    // Both of these islands already have a connection
                    continue;
                }
                connectedIslandIndices.Add(pair.IdxA);
                connectedIslandIndices.Add(pair.IdxB);
                var currentA = pair.ClosestA;
                int maxSteps = 100;
                while (maxSteps-- > 0) {
                    currentA = Pos.MoveTowards(currentA, pair.ClosestB);
                    if (currentA == pair.ClosestB) { break; }
                    if (!connectionCells.Contains(currentA)) {
                        connectionCells.Add(currentA);
                        Pos.ToXY(currentA, out var x, out var y);
                        spec.SetCell(x, y, CellType.Connection);
                    }
                }
                if (maxSteps < 1) {
                    D.Err("Fast connect island pairs went over 100 steps!");
                }
            }
        }

        private bool IsCovered(int i, int j) {
            var min = Mathf.Min(i, j);
            var max = Mathf.Max(i, j);
            var idx = string.Format("{0}-{1}", min, max);
            if (covered.Contains(idx)) { return true; }
            covered.Add(idx);
            return false;
        }

        private void FindClosestCells(ShipIsland islandA, ShipIsland islandB,
                out int closestA, out int closestB, out float distance) {
            var outermostA = islandA.OutermostCells;
            var outermostB = islandB.OutermostCells;
            var minDist = float.MaxValue;
            closestA = Pos.Invalid;
            closestB = Pos.Invalid;
            foreach (var cellA in outermostA) {
                foreach (var cellB in outermostB) {
                    if (!IsWithinGenArea(cellA)) { continue; }
                    if (!IsWithinGenArea(cellB)) { continue; }
                    var dist = Pos.Distance(cellA, cellB);
                    if (dist < minDist) {
                        closestA = cellA;
                        closestB = cellB;
                        minDist = dist;
                    }
                }
            }
            distance = minDist;
        }
    }
}
#endif
