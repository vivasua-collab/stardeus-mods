using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Grid;

namespace Game.Procgen {
    // A straight stretch of the original hull with open space outside it
    public readonly struct HullRun {
        // The first wall, lowest x on a north/south face, lowest y on an east/west one
        public readonly int Start;
        public readonly int Length;
        public readonly Facing.Type Outward;
        public readonly int Island;

        public HullRun(int start, int length, Facing.Type outward, int island) {
            Start = start;
            Length = length;
            Outward = outward;
            Island = island;
        }

        public int WallAt(int i) {
            return HullRuns.Along(Start, Outward, i);
        }
    }

    // Where things can be mounted on the outside of a generated ship so that they look like
    // part of its design (STA-1673): on the walls OuterWallGenStage built, never on a torn
    // edge or in the gap between two wreck sections. Runs after destruction, so it asks each
    // recorded hull wall whether it is still there and still faces open space.
    public static class HullRuns {
        private static readonly List<int> sorted = new();
        private static readonly HashSet<int> valid = new();

        // Runs facing `outward`, in position order. `clearance` tiles beyond every wall must
        // be empty space, so whatever goes there fits and does not sit against another section,
        // and none of them behind an engine (EngineExhaust).
        public static void Collect(GameState S, ShipSpec spec, Facing.Type outward,
                int clearance, HashSet<int> exhaust, List<HullRun> into) {
            valid.Clear();
            sorted.Clear();
            foreach (var wall in spec.HullWalls) {
                var pos = wall.PosIdx;
                if (IsHullWall(S, spec, wall, outward)
                        && IsClear(spec, pos, outward, clearance, exhaust)) {
                    valid.Add(pos);
                    sorted.Add(pos);
                }
            }
            // HashSet order must not leak into generation
            sorted.Sort();
            foreach (var pos in sorted) {
                // Only start a run where the previous wall along the face is not in one
                if (valid.Contains(Along(pos, outward, -1))) { continue; }
                var start = pos;
                var length = 1;
                while (valid.Contains(Along(pos, outward, length))) {
                    length++;
                }
                // A wall next to a door stays clear, so nothing hangs in front of an exit
                if (GenUtils.IsDoor(Along(pos, outward, length))) {
                    length--;
                }
                if (GenUtils.IsDoor(Along(pos, outward, -1))) {
                    start = Along(pos, outward, 1);
                    length--;
                }
                if (length <= 0) { continue; }
                var inward = Facing.NeighborAt(start, Facing.Opposite(outward));
                into.Add(new HullRun(start, length, outward, S.Sys.Areas.IslandAt(inward)));
            }
        }

        // A wall from the original hull that is still standing, intact, with the ship behind it
        // and space in front of it
        public static bool IsHullWall(GameState S, ShipSpec spec, Tile wall, Facing.Type outward) {
            if (wall == null || !wall.IsActive || wall.Door != null) { return false; }
            if (!spec.HullWalls.Contains(wall)) { return false; }
            var pos = wall.PosIdx;
            if (Pos.IsOOB(pos) || !spec.IsWithinGenArea(pos)) { return false; }
            // Replaced (airlock, window) or moved off without its grid slot
            if (EntityUtils.WallAt(pos) != wall) { return false; }
            if (wall.Health?.IsOperational == false) { return false; }
            var outside = Facing.NeighborAt(pos, outward);
            var inside = Facing.NeighborAt(pos, Facing.Opposite(outward));
            if (Pos.IsOOB(outside) || Pos.IsOOB(inside)) { return false; }
            if (EntityUtils.IsFloorAt(outside) || EntityUtils.IsWallAt(outside)) { return false; }
            // Ship behind it, or it is a wall left standing alone at a torn edge. An inner wall
            // meeting the hull counts, and so does a breached room: most wreck rooms are, and
            // asking for an indoor room left almost no hull
            return EntityUtils.IsFloorAt(inside);
        }

        public static bool IsHullWallAt(GameState S, ShipSpec spec, int pos, Facing.Type outward) {
            return IsHullWall(S, spec, EntityUtils.WallAt(pos), outward);
        }

        private static bool IsClear(ShipSpec spec, int wallPos, Facing.Type outward,
                int clearance, HashSet<int> exhaust) {
            var p = wallPos;
            for (int d = 1; d <= clearance; d++) {
                p = Facing.NeighborAt(p, outward);
                if (Pos.IsOOB(p) || !spec.IsWithinGenArea(p)) { return false; }
                if (exhaust != null && exhaust.Contains(p)) { return false; }
                if (EntityUtils.IsFloorAt(p) || EntityUtils.IsWallAt(p)
                        || EntityUtils.IsObjAt(p)) {
                    return false;
                }
            }
            return true;
        }

        // `steps` tiles along a face: east on a north/south face, north on an east/west one
        public static int Along(int pos, Facing.Type outward, int steps) {
            return IsHorizontal(outward)
                ? Pos.NeighborAt(pos, steps, 0)
                : Pos.NeighborAt(pos, 0, steps);
        }

        // True for north/south faces, whose runs go along x
        public static bool IsHorizontal(Facing.Type outward) {
            return outward == Facing.Type.Up || outward == Facing.Type.Down;
        }
    }
}
