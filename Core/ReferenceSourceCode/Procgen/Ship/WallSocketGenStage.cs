using System;
using System.Collections.Generic;
using System.Linq;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Utils;

namespace Game.Procgen {
    public class WallSocketGenStage : BaseShipGenStage {
        private readonly HashSet<int> allWalls = new();
        private readonly HashSet<int> visitedH = new();
        private readonly HashSet<int> visitedV = new();
        private readonly List<int> currentWall = new();
        private int leftmost;
        private int rightmost;
        public override void Apply() {
            // 1. get ALL walls
            for (int i = S.GridSize - 1; i >= 0; i--) {
                if (!IsWithinGenArea(i)) { continue; }
                var wall = EntityUtils.WallAt(i);
                if (wall != null) {
                    allWalls.Add(i);
                }
            }
            // 2. start with first wall piece
            foreach (int w in allWalls) {
                FillSocketsFor(w, Pos.W, Pos.E, visitedH);
                FillSocketsFor(w, Pos.S, Pos.N, visitedV);
            }

            // 3. find either vertical or diagonal non-interrupted line of wall (min 3 wall tiles)
            // 4. place a series of wall sockets along the wall starting from the middle and going to the sides
            // 5. repeat until we visited all the walls

        }

        private void FillSocketsFor(int startPos,
                Func<int, int> left, Func<int, int> right,
                HashSet<int> visited) {
            if (visited.Contains(startPos)) {
                return;
            }
            currentWall.Clear();
            leftmost = startPos;
            rightmost = startPos;

            if (!TryAddWall(startPos, visited)) {
                // no need to go left/right
                return;
            }

            // check right
            var cur = right(startPos);
            while (TryAddWall(cur, visited)) {
                rightmost = cur;
                cur = right(cur);
            }
            cur = left(startPos);
            while (TryAddWall(cur, visited)) {
                leftmost = cur;
                cur = left(cur);
            }
            // after this we have east-west continuation
            visited.AddRange(currentWall);
            BuildSocketsForCurrentWall(left, right);
        }

        private void BuildSocketsForCurrentWall(Func<int, int> left, Func<int, int> right) {
            if (currentWall.Count < 3) { return; }
            var centerPos = Pos.Between(leftmost, rightmost);
            var pos = centerPos;
            if (!TryBuildSocketAt(pos)) {
                pos = right(centerPos);
                if (!TryBuildSocketAt(pos)) {
                    pos = left(centerPos);
                    if (!TryBuildSocketAt(pos)) {
                        if (currentWall.Count > 4) {
                            pos = leftmost;
                        } else {
                            return; // give up. or not
                        }
                    }
                }
            }
            // traverse left and build sockets along the way
            TraverseExtraSocketsForCurrentWall(pos, left);
            // traverse right and build sockets along the way
            TraverseExtraSocketsForCurrentWall(pos, right);

        }
        private void TraverseExtraSocketsForCurrentWall(int pos, Func<int, int> next) {
            int attempts = 100;
            int stepsTillNextAttempt = 7;
            while (attempts-- > 0) {
                var distFromLastPos = 0;
                int prevPos = pos;
                for (int i = 0; i < stepsTillNextAttempt; i++) {
                    prevPos = pos;
                    pos = next(pos);
                    if (currentWall.Contains(pos)) {
                        distFromLastPos++;
                    } else {
                        if (distFromLastPos > 4) {
                            // attempt to build one more socket at the edge
                            TryBuildSocketAt(prevPos);
                        }
                        break;
                    }
                }
                if (!currentWall.Contains(pos)) {
                    break;
                }
                if (TryBuildSocketAt(pos)) {
                    stepsTillNextAttempt = 7;
                } else {
                    // try prev pos first
                    if (TryBuildSocketAt(prevPos)) {
                        pos = prevPos;
                        stepsTillNextAttempt = 7;
                    } else {
                        stepsTillNextAttempt = 1;
                    }
                }
            }
        }

        private bool TryBuildSocketAt(int pos) {
            var oldWall = EntityUtils.WallAt(pos);
            if (oldWall == null) {
                D.Err("Trying to build a socket where there is no wall! {0}", Pos.String(pos));
                return false;
            }
            // we won't replace anything electric with a socket
            if (oldWall.ENode != null) { return false; }

            bool isCloseToEnode = false;
            Pos.ForEachAround(pos, 1, false, true, (p) => {
                if (EntityUtils.ParentObjAt(p)?.ENode != null) {
                    isCloseToEnode = true;
                    return false;
                }
                return true;
            });
            if (isCloseToEnode) {
                return false;
            }

            string socketDefId = DefIds.StructureWallsWallSocket01;
            var oldDefId = oldWall.Definition.Id;
            if (oldDefId.Contains("Walls/Wall")) {
                socketDefId = oldWall.Definition.Id.Replace("Walls/Wall",
                    "Walls/WallSocket");
                if (The.Defs.TryGet(socketDefId) == null)  {
                    socketDefId = DefIds.StructureWallsWallSocket01;
                }
            }
            var socketProto = EntityUtils.CreatePrototype<Tile>(socketDefId);

            // Rotate the door so it has correct alignment with the walls
            var dnIdx = Pos.N(pos);
            var dsIdx = Pos.S(pos);
            if (EntityUtils.IsWallAt(dnIdx) && EntityUtils.IsWallAt(dsIdx)) {
                socketProto.Transform.Rotation = Facing.Type.Right;
            } else {
                socketProto.Transform.Rotation = Facing.Type.Down;
            }

            BuildTile(pos, socketProto);

            // D.Warn("Built a socket at {0}. Wall: {1}", Pos.String(pos),
            //     currentWall.Count);
            return true;
        }

        private bool TryAddWall(int pos, HashSet<int> visited) {
            if (!Pos.IsOOB(pos) && IsWithinGenArea(pos)
                    && allWalls.Contains(pos) && !visited.Contains(pos)) {


                int stretchCount = 0;
                if (allWalls.ContainsAll(Pos.N(pos), Pos.S(pos))) {
                    stretchCount++;
                }

                if (allWalls.ContainsAll(Pos.E(pos), Pos.W(pos))) {
                    stretchCount++;
                }

                if (stretchCount != 1) {
                    // either an intersection or an angled corner or and edge
                    return false;
                }

                int numAdjacentFloors = 0;
                Pos.ForEachAround(pos, 1, false, true, (p) => {
                    if (EntityUtils.IsEmptyFloorAt(p)) {
                        numAdjacentFloors++;
                    }
                    return true;
                });

                if (numAdjacentFloors == 0 || numAdjacentFloors % 3 != 0) {
                    return false;
                }

                // if (allWalls.ContainsCount(
                //         Pos.N(pos), Pos.S(pos), Pos.E(pos), Pos.W(pos)) > 2) {
                //     return false;
                // }
                currentWall.Add(pos);
                return true;
            }
            return false;
        }
    }
}
