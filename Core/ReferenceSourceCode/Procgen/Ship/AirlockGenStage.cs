using System.Collections.Generic;
using KL.Grid;
using Game.Data;
using System.Linq;
using Game.Constants;
using KL.Collections;
using Game.Components;
using Game.Commands;
using System.Collections;
using KL.Randomness;
using KL.Utils;

namespace Game.Procgen {
    [MustGoAfterGenStage(typeof(InnerDoorGenStage), typeof(MissingDoorGenStage))]
    // Will generate ship with seed: 1623935799 115x101 - blocked door
    public class AirlockGenStage : BaseShipGenStage {
        public override bool IsAsync => true;
        public override bool RequiresAreas => true;
        public override IEnumerator ApplyAsync() {
            var subRng = rng.Fork();
            var areas = new List<HashSet<int>>(S.Sys.Areas.Areas.Values);
            foreach (var a in areas) {
                TryBuildAirlockFor(a, subRng, true);
            }
            S.Sig.ForceAreas.Send();
            if (!S.Sys.Areas.HasUnreachableAreas) { yield break; }
            var unAreas = new List<HashSet<int>>();
            foreach (var a in S.Sys.Areas.DoorCheck.UnreachableAreas) {
                unAreas.Add(S.Sys.Areas.AreaById(a));
            }

            foreach (var area in unAreas) {
                TryBuildAirlockFor(area, subRng, false);
            }
        }

        private bool TryBuildAirlockFor(HashSet<int> area, Rng subRng,
                bool mustReachOOB) {
            if (!spec.IsWithinGenArea(area.First())) {
                return false;
            }
            subRng = subRng.Fork();
            var dirs = Facing.Types.OrderBy(v => subRng.Value);
            var areaSorted = new List<int>(area);
            areaSorted.Sort();
            foreach (var dir in dirs) {
                foreach (var pos in areaSorted) {
                    if (TryBuildAirlockInDirectionFrom(pos, dir, mustReachOOB,
                            out var changeDir)) {
                        return true;
                    } else if (changeDir) {
                        break; // while
                    }
                }
            }
            return false;
        }

        private bool TryBuildAirlockInDirectionFrom(int pos, Facing.Type dir,
                bool mustReachOOB, out bool changeDirection) {
            changeDirection = false;
            int lastWallAtPos = Pos.Invalid;
            int wallCount = 0;
            int startPos = pos;
            int startingArea = S.Sys.Areas.At(startPos);
            bool foundSpace = false;
            bool reachedOOB = false;
            // 1. we crawl tile by tile towards the dir until we reach either
            int attempts = S.GridHeight;
            while (attempts-- > 0) {
                pos = Facing.NeighborAt(pos, dir);
                // if we reach space we should keep going until we go OOB
                if (Pos.IsOOB(pos)) {
                    reachedOOB = true;
                    break;
                }
                if (EntityUtils.IsWallAt(pos)) {
                    if (foundSpace) {
                        // we found a wall beyond space
                        break;
                    }
                    var leftPos = Facing.LeftOf(pos, dir, 1);
                    var rightPos = Facing.RightOf(pos, dir, 1);
                    if (!EntityUtils.IsWallAt(leftPos) || !EntityUtils.IsWallAt(rightPos)) {
                        // diagonal, lets GTFO
                        return false;
                    }
                    if (++wallCount > 2)  {
                        // we may be traversing along the wall
                        return false;
                    }
                    lastWallAtPos = pos;
                    continue;
                }
                // other area or space
                var areaId = S.Sys.Areas.At(pos);
                if (areaId == 0) {
                    foundSpace = true;
                } else if (areaId != startingArea) {
                    // if we reach other area, we return false
                    return false;
                }
            }
            if (mustReachOOB && !reachedOOB) {
                changeDirection = true;
                return false;
            }
            if (foundSpace && lastWallAtPos != Pos.Invalid) {
                for (int i = 0; i < 4; i++) {
                    if (EntityUtils.IsObjAt(Pos.NeighborAt(lastWallAtPos, i))) {
                        //D.Warn("Found object near airlock at {0}, skipping",
                            //Pos.String(lastWallAtPos));
                        return false;
                    }
                }
                // good
                // if we got OOB, we backtrack to the last outer wall piece, replace it with airlock
                BuildTile(lastWallAtPos, newAirlockPrototype);
                var wallAt = EntityUtils.WallAt(lastWallAtPos);
                wallAt?.Door?.SetClosed(true);
                dir = Facing.Opposite(dir);
                pos = lastWallAtPos;
                // we keep backtracking and removing walls to remove double / triple walls along the way
                for (int i = 0; i < wallCount; i++) {
                    // go backwards to break through
                    pos = Facing.NeighborAt(pos, dir);
                    if (EntityUtils.IsWallAt(pos)) {
                        new CmdRemoveTile(pos, WorldLayer.Walls).Execute(S);
                    } else {
                        break; // to not go towards opposite wall if there is one
                    }
                }
                // we found a guaranteed
                if (!reachedOOB) {
                    changeDirection = true;
                    return false;
                }
                return true;
            }
            // if we don't got OOB we area in an indoors space pocket, we will return false
            return false;
        }

        private Tile newAirlockPrototype => EntityUtils.CreatePrototype<Tile>(
            DefIds.StructureDoorsDoorAirlock01);
    }
}
