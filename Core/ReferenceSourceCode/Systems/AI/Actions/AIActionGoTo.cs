using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionGoTo : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionGoTo());
        }
        public const string ActId = "GoTo";
        public static readonly int ActIdH = Hashes.S(ActId);
        private AIActionGoTo() : base(ActId, () => T.ActMovenexttopos) {
            // Preconditions = AIState.With(AIVarsH.IsNear, false);
            Outcomes = AIState.With(AIVarsH.IsNear, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Int1 = 0;
            agent.BB.MoveProgress = null;
        }
        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Int1 = 0;
            agent.BB.MoveProgress = null;
        }
        private static PathReqFlags PathFlags(AIGoal goal) {
            var pathFlags = goal.PathFlags;
            if ((pathFlags & (PathReqFlags.TryWorkSpot | PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby))
                    == PathReqFlags.None) {
                pathFlags |= PathReqFlags.AdjOnTop;
                pathFlags |= PathReqFlags.AdjNearby;
            }
            return pathFlags;
        }
        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            var nav = agent.Motion;
            // wait for it to stop
            if (nav.IsBusy) {
                return InProgress;
            }

            var entity = goal.Target.Entity;

            if (entity is Tile tile) {
                if (tile.Definition.LayerId == WorldLayer.Floor) {
                    var objAbove = EntityUtils.ParentObjAt(tile.PosIdx);
                    // can't reach obj below, let's reach obj above
                    if (objAbove != null && objAbove.IsConstructed) {
                        entity = objAbove;
                        tile = objAbove;
                    }
                }
            } else {
                tile = null;
            }

            // request motion
            if (agent.BB.MoveProgress == null) {
                if (entity.PosIdx == nav.Position.Idx) {
                    // we are on top, but we want to be near. let's find a position
                    // nearby
                    var found = Pos.Around(entity.PosIdx, Caches.PosIdx9, false);
                    for (int i = 0; i < found; i++) {
                        var pos = Caches.PosIdx9[i];
                        if (S.Sys.Path.IsRoamable(pos, nav.IsGrounded)) {
                            if (nav.TryGoTo(pos, PathFlags(goal),
                                    out agent.BB.MoveProgress)) {
                                return InProgress;
                            }
                        }
                    }
                    // we couldn't find the way out
                    // in that case we will assert that it's ok to be on top
                    goal.WithWorld(AIVarsH.IsNear, true);
                    return Success;
                }
                var flags = PathFlags(goal);
                if (tile != null) {
                    if ((flags & PathReqFlags.TryWorkSpot) == PathReqFlags.TryWorkSpot) {
                        if (tile.Transform.HasWorkSpot) {
                            var ws = tile.Transform.WorkSpot;
                            flags |= PathReqFlags.AdjOnTop;
                            flags &= ~PathReqFlags.AdjNearby;
                            if (nav.TryGoTo(ws, flags, out agent.BB.MoveProgress)) {
                                return InProgress;
                            } else {
                                // nav.StoreNavFailureAt(ws);
                                nav.StoreNavFailureAt(tile.PosIdx);
                                return FailedNavToTarget(agent, ws);
                            }
                        } else {
                            flags |= PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby;
                        }
                    }
                    // If this is set to OnTop, deconstructing floors
                    // for grounded workers will never work properly
                    if (nav.TryGoTo(tile, flags, out agent.BB.MoveProgress)) {
                        agent.BB.MoveTargetPos = tile.PosIdx;
                        return InProgress;
                    } else {
                        // nav.StoreNavFailureAt(tile.PosIdx);
                        return FailedNavToTarget(agent, tile.PosIdx);
                    }
                } else {
                    if (nav.TryGoTo(entity.PosIdx, flags, out agent.BB.MoveProgress)) {
                        agent.BB.MoveTargetPos = entity.PosIdx;
                        return InProgress;
                    } else {
                        // nav.StoreNavFailureAt(entity.PosIdx);
                        return FailedNavToTarget(agent, entity.PosIdx);
                    }
                }
            }

            // check result
            if (tile != null) {
                if (EntityUtils.IsNearObj(nav.Position.Idx, tile)) {
                    // we have arrived
                    // cannot set world state is near, because
                    // if task fails later, another agent
                    // will still have to get near
                    goal.WithWorld(AIVarsH.IsNear, true);
                    return Success;
                }
                nav.StoreNavFailureAt(tile.PosIdx);
                return FailedNavToTarget(agent, tile.PosIdx);
            } else {
                if (Pos.IsNear(nav.Position.Idx, entity.PosIdx, 1, 1)) {
                    goal.WithWorld(AIVarsH.IsNear, true);
                    return Success;
                }
                if (entity is Being b) {
                    if (agent.BB.Int1++ < 3) {
                        // try to catch up
                        agent.BB.MoveProgress = null;
                        return InProgress;
                    }
                }
                nav.StoreNavFailureAt(agent.BB.MoveTargetPos);
                return FailedNavToTarget(agent);
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target != null) {
                return agent.Motion.GoToCost(goal.Target.Entity.PosIdx);
            } else {
                return agent.Motion.GoToCost(Pos.Invalid);
            }
        }
    }
}
