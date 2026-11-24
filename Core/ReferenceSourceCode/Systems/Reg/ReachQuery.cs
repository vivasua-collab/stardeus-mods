using Game.Data;
using Game.Systems.Areas;
using KL.Grid;
using Unity.Collections;

namespace Game.Systems.Reg {
    // One being's reach, taken once per target search (BeingReach.For) and asked per
    // candidate. A position counts when the being can stand on it or next to it.
    public readonly struct ReachQuery {
        // null: judge by floor islands (the being stands where reach can't be told)
        private readonly int[] labels;
        private readonly NativeArray<int> posRegIds;
        private readonly int start;
        private readonly AreaSys areas;
        private readonly int island;
        private readonly bool isGrounded;

        public ReachQuery(int[] labels, NativeArray<int> posRegIds, int start) {
            this.labels = labels;
            this.posRegIds = posRegIds;
            this.start = start;
            areas = null;
            island = 0;
            isGrounded = false;
        }

        private ReachQuery(AreaSys areas, int island, bool isGrounded) {
            labels = null;
            posRegIds = default;
            start = -1;
            this.areas = areas;
            this.island = island;
            this.isGrounded = isGrounded;
        }

        public static ReachQuery ByIslands(AreaSys areas, int pos, bool isGrounded) {
            var island = isGrounded && !Pos.IsOOB(pos) ? areas.IslandAt(pos) : 0;
            return new ReachQuery(areas, island, isGrounded);
        }

        public bool IsByIslands => labels == null;

        public bool CanReach(int pos) {
            if (Pos.IsOOB(pos)) { return false; }
            if (labels == null) {
                return !isGrounded || areas.CanReachGroundedIslandToPos(island, pos);
            }
            var r = posRegIds[pos];
            if (r >= 0) {
                var l = labels[r];
                if (l == start) { return true; }
                if (l >= 0) { return false; }
            }
            // a wall, a device or unsafe air: worked from a tile next to it
            for (int i = 0; i < 8; i++) {
                var n = Pos.NeighborAt(pos, i);
                if (Pos.IsOOB(n)) { continue; }
                var nr = posRegIds[n];
                if (nr >= 0 && labels[nr] == start) { return true; }
            }
            return false;
        }

        public bool CanReach(Entity entity) {
            if (labels != null && entity is Tile tile && tile.Transform.IsMulti) {
                foreach (var p in tile.Transform.Outline) {
                    if (CanReach(p)) { return true; }
                }
                return false;
            }
            return CanReach(entity.PosIdx);
        }
    }
}
