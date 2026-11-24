using System.Collections.Generic;
using System.Linq;
using Game.Data;
using Game.Systems.Naming;
using Game.Utils;
using KL.Collections;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    public static class GenUtils {
        public static HashSet<int> FindEdge(HashSet<int> island) {
            var res = new HashSet<int>();
            foreach (var pos in island) {
                if (!EntityUtils.IsSurroundedBySolidFloor(pos)) {
                    res.Add(pos);
                }
            }
            return res;
        }

        // The round CPU room InnerWallGenStage walls in at the ship's center. Its invincible
        // disc reaches a tile past the ring here and there, so "the area's first tile is
        // invincible" also caught neighbors, which then got no rooms and no doors.
        public static bool IsCenterRoom(ShipSpec spec, HashSet<int> area) {
            var center = Pos.FromXY(spec.CenterX, spec.CenterY);
            return spec.InvincibleParts.Contains(center) && area.Contains(center);
        }

        // A door tile, asked of the tile itself. EntityUtils.IsDoorAt reads the path system's
        // door map, which lags behind the doors generation placed moments ago: vents and
        // windows replaced fresh doors, and storages were built against them.
        public static bool IsDoor(int pos) {
            return EntityUtils.WallAt(pos)?.Door != null;
        }

        // The crew is spawned before the late placement stages. A device built over a being
        // shuts it in, and it hops out of it on its first path with an "Unstucking" error.
        public static bool CoversBeing(GameState s, Tile tile, int pos) {
            int cnt = tile.Transform.GetSubtilePos(pos, Caches.PosIdx);
            for (int i = 0; i < cnt; i++) {
                if (s.Beings.FindFirstInPos(Caches.PosIdx[i]) != null) {
                    return true;
                }
            }
            return false;
        }

        // The shuttle is placed by its spot, not around the crew, so a being already standing
        // there is shut in. Moving it to the port it boards from keeps the curated wrecks
        // unchanged.
        public static void MoveBeingsToOutPort(GameState s, Tile tile) {
            var exit = tile.Transform.OutPort;
            if (Pos.IsOOB(exit)) { return; }
            int cnt = tile.Transform.GetSubtilePos(tile.PosIdx, Caches.PosIdx);
            var beings = s.Beings.Alive;
            for (int i = 0; i < beings.Count; i++) {
                var being = beings[i];
                if (!being.IsActive) { continue; }
                for (int j = 0; j < cnt; j++) {
                    if (Caches.PosIdx[j] == being.PosIdx) {
                        being.Motion.Abort("Moved out of a generated shuttle");
                        being.Motion.TeleportTo(EntityUtils.CenterOf(exit));
                        break;
                    }
                }
            }
        }

        public static bool ContainsAny(HashSet<int> area, List<int> positions) {
            foreach (var p in positions) {
                if (area.Contains(p)) { return true; }
            }
            return false;
        }

        public static void FillWallGaps(ICollection<int> walls) {
            var missingWalls = new HashSet<int>();

            foreach (var wall in walls) {
                var n = Pos.NeighborAt(wall, 0, 1);
                var s = Pos.NeighborAt(wall, 0, -1);
                var e = Pos.NeighborAt(wall, 1, 0);
                var w = Pos.NeighborAt(wall, -1, 0);
                var hasN = walls.Contains(n) || missingWalls.Contains(n);
                var hasS = walls.Contains(s) || missingWalls.Contains(s);
                var hasE = walls.Contains(e) || missingWalls.Contains(e);
                var hasW = walls.Contains(w) || missingWalls.Contains(w);
                var count = 0;
                if (hasN) { count++; }
                if (hasS) { count++; }
                if (hasE) { count++; }
                if (hasW) { count++; }
                if (count == 1) {
                    // We need to get the diagonals to figure out where to place
                    // a missing wall
                    var ne = Pos.NeighborAt(wall, 1, 1);
                    var se = Pos.NeighborAt(wall, 1, -1);
                    var nw = Pos.NeighborAt(wall, -1, 1);
                    var sw = Pos.NeighborAt(wall, -1, -1);
                    var hasNE = walls.Contains(ne) || missingWalls.Contains(ne);
                    var hasNW = walls.Contains(nw) || missingWalls.Contains(nw);
                    var hasSE = walls.Contains(se) || missingWalls.Contains(se);
                    var hasSW = walls.Contains(sw) || missingWalls.Contains(sw);
                    if (!hasN && (hasNE || hasNW)) {
                        missingWalls.Add(n);
                    } else if (!hasS && (hasSE || hasSW)) {
                        missingWalls.Add(s);
                    } else if (!hasE && (hasNE || hasNW)) {
                        missingWalls.Add(e);
                    } else if (!hasW && (hasNW || hasSW)) {
                        missingWalls.Add(w);
                    } else {
                        #if DEBUG_GEN
                        // D.Warn(string.Format("Bad missing wall combo! "
                            + " {8}: n:{0},e:{1},s:{2},w:{3},"
                            + "ne:{4},nw:{5},se:{6},sw:{7}",
                            hasN, hasE, hasS, hasW,
                            hasNE, hasNW, hasSE, hasSW, wall));
                        #endif
                    }
                }
            }

            foreach (var wall in missingWalls) {
                walls.Add(wall);
            }

        }

        public static string NewGenericName(GameState s) {
            var result = s.Query.GenerateName.Ask(NameRequest.Generic(NameType.Seed));
            return result.Name;
        }
        public static string NewShipComputerName(GameState s) {
            var result = s.Query.GenerateName.Ask(NameRequest.ForShipOS());
            return result.Name;
        }

        public static string NewGameShipName(GameState s) {
            var result = s.Query.GenerateName.Ask(
                NameRequest.Generic(NameType.Ship));
            var name = result.Name;
            int num = 2;
            int attempts = 50;
            while (attempts-- > 50 && SaveLoadUtils.AvailableSaves.Count(
                    s => s.GameName == name) != 0) {
                name = $"{name} {num++}";
            }
            if (attempts < 1) {
                name = The.Platform.PlayerName;
            }

            return name;
        }
    }
}
