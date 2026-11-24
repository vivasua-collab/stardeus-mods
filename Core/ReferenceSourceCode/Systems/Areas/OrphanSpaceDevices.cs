using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Grid;

namespace Game.Systems.Areas {
    // Space devices floating in the build margin with no section to belong to. Abandoning a
    // derelict used to clear only a ring around its floors, so its chained solar panels and
    // overhanging thrusters stayed behind for good (STA-1912). Flights and hyperjumps take
    // them along with the derelicts. A device counts when nothing connects it to a section:
    // no chain of devices and floors from it reaches an island. Players cannot build outside
    // the margin, so only devices there are candidates.
    public static class OrphanSpaceDevices {
        [SelfInit]
        private static void Cleanup() {
            frontier.Clear();
            visited.Clear();
            group.Clear();
        }

        private static readonly Queue<int> frontier = new();
        private static readonly HashSet<int> visited = new();
        private static readonly List<Tile> group = new();

        // Fills `into` with the root tile of every orphan device outside the margin
        public static void Collect(GameState s, List<Tile> into) {
            into.Clear();
            visited.Clear();
            var areas = s.Sys.Areas;
            foreach (var t in s.DataGrids[WorldLayer.Objects].Data) {
                if (t == null || t.Transform.IsChild || !t.IsConstructed) { continue; }
                if (!t.Constructable.IsBuiltInSpace) { continue; }
                if (visited.Contains(t.PosIdx)) { continue; }
                if (EntityUtils.IsWithinMargin(t.PosIdx, false, false)) { continue; }
                if (areas.IslandAt(t.PosIdx) != 0) { continue; }
                if (!ReachesIsland(areas, t)) {
                    foreach (var g in group) {
                        if (!EntityUtils.IsWithinMargin(g.PosIdx, false, false)) {
                            into.Add(g);
                        }
                    }
                }
            }
            frontier.Clear();
            group.Clear();
        }

        // Every cell the devices occupy, for CmdAbandonSection
        public static HashSet<int> CellsOf(List<Tile> devices) {
            var cells = new HashSet<int>();
            foreach (var t in devices) {
                var count = t.Transform.GetSubtilePos(Caches.PosIdx);
                for (int i = 0; i < count; i++) {
                    cells.Add(Caches.PosIdx[i]);
                }
            }
            return cells;
        }

        // Floods the structure the device is part of: devices and floors touching each other,
        // diagonals included (that is how the winch attaches them, SectionMoveSet)
        private static bool ReachesIsland(AreaSys areas, Tile start) {
            frontier.Clear();
            group.Clear();
            AddDevice(start);
            while (frontier.Count > 0) {
                var c = frontier.Dequeue();
                Pos.ToXY(c, out var cx, out var cy);
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dy = -1; dy <= 1; dy++) {
                        if (dx == 0 && dy == 0) { continue; }
                        // by coordinates: the map edge is right there, and an index step
                        // past it wraps into the next column
                        if (Pos.IsOOBXY(cx + dx, cy + dy)) { continue; }
                        var n = Pos.FromXY(cx + dx, cy + dy);
                        if (visited.Contains(n)) { continue; }
                        if (areas.IslandAt(n) != 0) { return true; }
                        var obj = EntityUtils.ParentObjAt(n);
                        if (obj != null) {
                            AddDevice(obj);
                        } else if (EntityUtils.IsFloorAt(n)) {
                            visited.Add(n);
                            frontier.Enqueue(n);
                        }
                    }
                }
            }
            return false;
        }

        private static void AddDevice(Tile t) {
            var count = t.Transform.GetSubtilePos(Caches.PosIdx);
            for (int i = 0; i < count; i++) {
                var p = Caches.PosIdx[i];
                if (visited.Add(p)) {
                    frontier.Enqueue(p);
                }
            }
            if (t.IsConstructed && t.Constructable.IsBuiltInSpace) {
                group.Add(t);
            }
        }
    }
}
