using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.Ships;
using KL.Grid;
using KL.Pool;
using KL.Utils;

namespace Game.Systems.Areas {
    // Screen edge arrows pointing at ship sections stranded in the corner no-fly
    // zones, where derelict ships spawn. A section qualifies only while every one
    // of its tiles sits inside one corner rect, which is also what makes it
    // something a hyperjump would leave behind.
    public sealed class CornerDerelictMarkers {
        private const int CornerCount = 4;

        private readonly GameState S;
        private readonly ShipOffScreenMarker[] markers =
            new ShipOffScreenMarker[CornerCount];
        private readonly bool[] hasTarget = new bool[CornerCount];
        private readonly int[] minX = new int[CornerCount];
        private readonly int[] minY = new int[CornerCount];
        private readonly int[] maxX = new int[CornerCount];
        private readonly int[] maxY = new int[CornerCount];

        public CornerDerelictMarkers(GameState s) {
            S = s;
            var prefab = ResourceLibrary.FindPrefab<ShipOffScreenMarker>();
            for (int i = 0; i < CornerCount; i++) {
                var marker = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                    ResourceLibrary.Root);
                marker.SetupAsCornerMarker();
                markers[i] = marker;
            }
        }

        public void Refresh() {
            for (int i = 0; i < CornerCount; i++) {
                hasTarget[i] = false;
            }
            // A lone island is the whole ship, and a jump never abandons that
            if (S.Sys.Areas.IslandCount > 1) {
                foreach (var isle in S.Sys.Areas.Islands) {
                    CollectIsland(isle.Value);
                }
            }
            for (int i = 0; i < CornerCount; i++) {
                if (hasTarget[i]) {
                    markers[i].SetCornerTarget(
                        new PosRect(minX[i], minY[i], maxX[i], maxY[i]));
                } else {
                    markers[i].ClearCornerTarget();
                }
            }
        }

        private void CollectIsland(HashSet<int> isle) {
            var corner = -1;
            var isleMinX = int.MaxValue;
            var isleMinY = int.MaxValue;
            var isleMaxX = int.MinValue;
            var isleMaxY = int.MinValue;
            foreach (var p in isle) {
                Pos.ToXY(p, out var x, out var y);
                var c = CornerAt(x, y);
                // The hull normally fails on its first tile, which is what keeps
                // this cheap enough to run on every area recalculation
                if (c < 0 || (corner >= 0 && c != corner)) { return; }
                corner = c;
                if (x < isleMinX) { isleMinX = x; }
                if (x > isleMaxX) { isleMaxX = x; }
                if (y < isleMinY) { isleMinY = y; }
                if (y > isleMaxY) { isleMaxY = y; }
            }
            if (corner < 0) { return; }
            if (!hasTarget[corner]) {
                hasTarget[corner] = true;
                minX[corner] = isleMinX;
                minY[corner] = isleMinY;
                maxX[corner] = isleMaxX;
                maxY[corner] = isleMaxY;
                return;
            }
            if (isleMinX < minX[corner]) { minX[corner] = isleMinX; }
            if (isleMinY < minY[corner]) { minY[corner] = isleMinY; }
            if (isleMaxX > maxX[corner]) { maxX[corner] = isleMaxX; }
            if (isleMaxY > maxY[corner]) { maxY[corner] = isleMaxY; }
        }

        // Matches the corner ranges EntityUtils.IsWithinMargin rejects for a 1x1
        // footprint. Returns -1 when the tile is outside every corner.
        private static int CornerAt(int x, int y) {
            var m = Consts.CornerMargin;
            var isLeft = x < m;
            var isRight = x >= Pos.GridW - m;
            var isBottom = y < m;
            var isTop = y >= Pos.GridH - m;
            if (isLeft && isBottom) { return 0; }
            if (isRight && isBottom) { return 1; }
            if (isLeft && isTop) { return 2; }
            if (isRight && isTop) { return 3; }
            return -1;
        }

        public void Dispose() {
            for (int i = 0; i < CornerCount; i++) {
                if (markers[i] == null) { continue; }
                PrefabPool.Despawn(markers[i].gameObject);
                markers[i] = null;
            }
        }
    }
}
