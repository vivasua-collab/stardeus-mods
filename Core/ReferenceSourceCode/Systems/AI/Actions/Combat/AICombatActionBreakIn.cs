using System;
using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Reg;
using UnityEngine.InputSystem;
using Game.Systems.Motion;
using Game.Systems.Areas;

namespace Game.Systems.AI {
    public sealed class AICombatActionBreakIn : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionBreakIn());
        }
        public const string ActId = "BreakIn";
        /// <summary>
        /// This action is going to try to break in
        /// </summary>
        private AICombatActionBreakIn() : base(ActId, () => T.CombatStateBreakin) {
            Preconditions = AIState.With(AIVarsH.HasSearchedForEnemy, false);
            Outcomes = AIState.With(AIVarsH.HasSearchedForEnemy, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            // Attempt to find a door that is locked or has an enemy filter and leads to an enemy
            var motion = agent.Motion;
            var cu = agent.CombatUnit;
            var target = cu.AttackTarget;
            if (target == null && agent.BB.TargetTile != null
                    && BreachRoute.IsReplanDue(agent, ticks)
                    && Replan(S, agent, goal, ticks)) {
                return Success;
            }
            if (motion.IsBusy) {
                if (target != null && cu.HasLineOfSight) {
                    goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
                    return Success;
                } else {
                    return InProgress;
                }
            }

            if (target != null) {
                goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
                return Success;
            }

            if (!agent.BB.Bool1) {
                agent.BB.Bool1 = true;
                BreachRoute.DelayReplan(agent, ticks);
                var way = BreachRoute.TowardNearestEnemy(S, agent, out var door);
                if (way == BreachRoute.Way.Open && TryLeaveForOpenWay(agent, goal, ticks)) {
                    return Success;
                }
                if (way == BreachRoute.Way.Door && TryTargetDoor(agent, door)) {
                    return InProgress;
                }
                // no enemy, only walls in the way, or the search fails to path where the
                // regions see a way
                return FindBreakInTile(S, agent, cu, motion, false);
            }

            var tt = agent.BB.TargetTile;

            if (tt != null) {
                if (!tt.IsActive || tt.Health.IsDestroyed || !tt.Constructable.IsConstructed
                        || Positions.NoLongerBlocks(tt, motion)) {
                    goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
                    return Success;
                }
                if (EntityUtils.IsNearObj(agent.PosIdx, tt)) {
                    var bestPunch = cu.BestMeleeAttack(true);
                    if (bestPunch == null) {
                        return Failed("Can't punch the wall!");
                    }
                    bestPunch.TryAttack(S, cu, tt, bestPunch.MaxAccuracyDist, ticks,
                        0f, out _, out _);
                    return InProgress;
                } else {
                    // failed to reach
                    agent.BB.TargetTile = null;
                }
            }

            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer10Minutes, Consts.TicksPer30Minutes);
            return Failed("Could not break in!");
        }

        // true: an enemy can be walked to now, SearchForEnemy takes over. Otherwise a door that
        // opens nothing new any more (a room it can already walk into) gives way to the door
        // on the route
        private bool Replan(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var way = BreachRoute.TowardNearestEnemy(S, agent, out var door);
            if (way == BreachRoute.Way.Open) {
                return TryLeaveForOpenWay(agent, goal, ticks);
            }
            var tt = agent.BB.TargetTile;
            if (way == BreachRoute.Way.Door && door != tt
                    && BreachRoute.LeadsNowhereNew(S, agent, tt)) {
                agent.Motion.Clear(resetFlags: true);
                TryTargetDoor(agent, door);
            }
            return false;
        }

        // Not while the search keeps failing to path where the regions see a way: the unit
        // would only bounce between the two actions
        private static bool TryLeaveForOpenWay(AIAgentComp agent, AIGoal goal, long ticks) {
            var bb = agent.BB;
            if (bb.IsCoolingDown(AICombatActionSearchForEnemy.PathFailedCooldownIdH, ticks)) {
                return false;
            }
            bb.Cooldowns.Remove(AICombatActionSearchForEnemy.CooldownIdH);
            agent.Motion.Clear(resetFlags: true);
            goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
            return true;
        }

        private bool TryTargetDoor(AIAgentComp agent, Tile door) {
            agent.BB.TargetTile = door;
            agent.BB.MoveProgress = null;
            if (EntityUtils.IsNearObj(agent.PosIdx, door)
                    || TryGoToOnce(agent, door, true, PathReqFlags.NonDiagonal | PathReqFlags.AdjNearby)) {
                return true;
            }
            agent.BB.TargetTile = null;
            agent.BB.MoveProgress = null;
            return false;
        }

        private AIActionResult FindBreakInTile(GameState S, AIAgentComp agent,
                CombatUnit cu, MotionControl motion, bool fallback) {
            // 1. if we are locked in a room, try to break through a wall or door
            var areas = S.Sys.Areas;
            var curAreaId = areas.At(cu.PosIdx);
            Caches.IntSet.Clear();
            bool addedWalls = false;
            areas.OutlineOfArea(curAreaId, Caches.IntSet);
            Caches.TileSet.Clear();
            if (!fallback) {
                if (S.Sys.Areas.HasUnreachableAreas) {
                    if (!S.Sys.Areas.IsAreaReachable(curAreaId)) {
                        // we are trapped, let's try to break through
                        // pick what to break
                        foreach (var p in Caches.IntSet) {
                            var wall = EntityUtils.WallAt(p);
                            if (wall == null || !wall.IsConstructed) {
                                continue;
                            }
                            if (!TouchesAreaOrthogonally(areas, curAreaId, p)) {
                                continue;
                            }
                            var door = wall.Door;
                            if (door != null) {
                                // can we go through?
                                if (door.IsLocked || door.IsDeniedFor(motion)) {
                                    if (TryAddDoor(areas, curAreaId, p, wall)) {
                                        continue;
                                    }
                                }
                            }
                        }
                        if (Caches.TileSet.Count == 0) {
                            // no doors!
                            addedWalls = true;
                            AddWalls(agent, curAreaId);
                        }
                    }
                } else {
                    var doors = S.Components.AllByType<DoorComp>();
                    if (doors != null) {
                        foreach (DoorComp door in doors) {
                            if (!door.Tile.IsConstructed) { continue; }
                            if (door.IsLocked || door.IsDeniedFor(motion)) {
                                var wall = door.Tile;
                                if (TryAddDoor(areas, curAreaId, wall.PosIdx, wall)) {
                                    continue;
                                }
                            }
                        }

                    }
                }
            }

            if (!addedWalls && Caches.TileSet.Count == 0) {
                // no doors, try all walls in current area!
                addedWalls = true;
                AddWalls(agent, curAreaId);
            }

            var targetCnt = Caches.TileSet.Count;
            if (targetCnt <= 0) {
                return InProgress;
            }
            var tilePool = ArrayPool<Tile>.Shared;
            var tiles = tilePool.Rent(targetCnt);
            int i = 0;
            foreach (var t in Caches.TileSet) {
                tiles[i++] = t;
            }
            Tile closestTile = RegUtils.FindClosestReachable(S, motion.Position.Idx,
                motion.IsGrounded, tiles, i, t => t.PosIdx, onTop: false);

            bool retryWithFallback = !fallback && !addedWalls;
            if (closestTile != null) {
                // We need to set this tile as an attack target.
                agent.BB.TargetTile = closestTile;
                agent.BB.MoveProgress = null;
                if (!TryGoToOnce(agent, closestTile, true, PathReqFlags.NonDiagonal | PathReqFlags.AdjNearby)) {
                    var backup = tiles[Rng.URange(0, targetCnt)];
                    agent.BB.MoveProgress = null;
                    if (TryGoToOnce(agent, backup, true, PathReqFlags.AdjNearby)) {
                        retryWithFallback = false;
                    }
                } else {
                    retryWithFallback = false;
                }
            }
            tilePool.Return(tiles, true);
            if (retryWithFallback) {
                return FindBreakInTile(S, agent, cu, motion, true);
            }
            return InProgress;
        }

        // The outline also holds diagonal neighbours, but the unit has to stand orthogonally
        // next to what it breaks: the path there is NonDiagonal, and a hole reached only
        // diagonally stays blocked by the corner walls (STA-1609)
        private static bool TouchesAreaOrthogonally(AreaSys areas, int areaId, int p) {
            return IsFloorOf(areas, areaId, Pos.E(p)) || IsFloorOf(areas, areaId, Pos.W(p))
                || IsFloorOf(areas, areaId, Pos.S(p)) || IsFloorOf(areas, areaId, Pos.N(p));
        }

        private static bool IsFloorOf(AreaSys areas, int areaId, int p) {
            return areas.At(p) == areaId && areas.AreaTypeAt(p) == AreaType.Floor;
        }

        private static bool TryAddDoor(AreaSys areas, int curAreaId, int p, Tile wall) {
            var id = areas.At(Pos.E(p));
            if ((id > 0 || id < -1) && id != curAreaId) {
                Caches.TileSet.Add(wall);
                return true;
            }
            id = areas.At(Pos.W(p));
            if ((id > 0 || id < -1) && id != curAreaId) {
                Caches.TileSet.Add(wall);
                return true;
            }
            id = areas.At(Pos.S(p));
            if ((id > 0 || id < -1) && id != curAreaId) {
                Caches.TileSet.Add(wall);
                return true;
            }
            id = areas.At(Pos.N(p));
            if ((id > 0 || id < -1) && id != curAreaId) {
                Caches.TileSet.Add(wall);
                return true;
            }

            return false;
        }

        private static void AddWalls(AIAgentComp agent, int curAreaId) {
            bool foundWallToEnclosed = false;
            var areas = agent.S.Sys.Areas;
            foreach (var p in Caches.IntSet) {
                var wall = EntityUtils.WallAt(p);
                if (wall != null && wall.IsConstructed
                        && TouchesAreaOrthogonally(areas, curAreaId, p)) {
                    var id = areas.At(Pos.E(p));
                    if (id > 0 && id != curAreaId) {
                        foundWallToEnclosed = true;
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                    id = areas.At(Pos.W(p));
                    if (id > 0 && id != curAreaId) {
                        foundWallToEnclosed = true;
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                    id = areas.At(Pos.S(p));
                    if (id > 0 && id != curAreaId) {
                        foundWallToEnclosed = true;
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                    id = areas.At(Pos.N(p));
                    if (id > 0 && id != curAreaId) {
                        foundWallToEnclosed = true;
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                }
            }
            if (foundWallToEnclosed) { return; }
            foreach (var p in Caches.IntSet) {
                var wall = EntityUtils.WallAt(p);
                if (wall != null && wall.IsConstructed
                        && TouchesAreaOrthogonally(areas, curAreaId, p)) {
                    var id = areas.At(Pos.E(p));
                    if (id < -1 && id != curAreaId) {
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                    id = areas.At(Pos.W(p));
                    if (id < -1 && id != curAreaId) {
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                    id = areas.At(Pos.S(p));
                    if (id < -1 && id != curAreaId) {
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                    id = areas.At(Pos.N(p));
                    if (id < -1 && id != curAreaId) {
                        Caches.TileSet.Add(wall);
                        continue;
                    }
                }
            }
            // We don't want to break all the walls for no reason
            // if (Caches.TileSet.Count == 0) {
            //     D.Err("Fallback - adding any wall");
            //     foreach (var p in Caches.IntSet) {
            //         var wall = EntityUtils.WallAt(p);
            //         if (wall != null && wall.IsConstructed) {
            //             Caches.TileSet.Add(wall);
            //         }
            //     }
            // }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // players won't break in
            if (agent.BeingData.EffectiveFaction.IsPlayer) {
                return 0f;
            }
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            // NO! we could be trapped with no doors
            // no doors available!
            // if (agent.S.Sys.Areas.DoorCheck.DoorsByPos.Count == 0) {
            //     agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer30Minutes,
            //         Consts.TicksPerHour);
            //     return 0f;
            // }
            return agent.Motion.GoToCost(Pos.Invalid) * 3f;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
