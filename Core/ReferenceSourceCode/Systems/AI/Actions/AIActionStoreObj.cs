using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using KL.Grid;
using Game.Systems.Items;

namespace Game.Systems.AI {
    public sealed class AIActionStoreObj : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStoreObj());
        }
        public const string ActId = "StoreObj";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionStoreObj() : base(ActId, () => T.ActStoreitem) {
            Preconditions = AIState.With(AIVarsH.IsObjStored, false);
            Outcomes = AIState.With(AIVarsH.IsObjStored, true);
            WithRequiredJobType(JobTypeIdH.Hauling);
            WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // is near and carrying
            agent.BB.Bool1NotSaved = false;
            // obj we are storing
            agent.BB.TargetObjData = null;
            // for storages
            agent.BB.ClearBlacklist();
            // target storage
            agent.BB.TargetTile = null;
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Bool1NotSaved) {
                agent.Being.Graphics.DropIfCarrying();
                agent.BB.Bool1NotSaved = false;
            }
            if (agent.BB.TargetObjData != null) {
                agent.BB.TargetObjData.TryUnreserve(goal);
                agent.BB.TargetObjData = null;
            }
            agent.BB.TargetTile = null;
            agent.BB.ClearBlacklist();
            agent.BB.MoveTargetPos = 0;
            agent.BB.MoveProgress = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if (agent.IsFeelingSafeNow) {
                    return InProgress;
                } else {
                    return Failed(T.InfoUnsafeConditions);
                }
            }

            var agentPos = agent.PosIdx;


            var objData = agent.BB.TargetObjData;
            if (objData == null) {
                return ReserveObj(agent, goal, ref objData);
            }

            // we're near
            if (!agent.BB.Bool1NotSaved) {
                return PickUpObj(agent, objData, agentPos);
            }

            if (agent.BB.TargetTile == null) {
                return FindStorage(S, agent, objData, agentPos, goal, ticks);
            }

            // check if we are at the storage
            if (agentPos != agent.BB.TargetTile.Transform.InPort) {
                // failed to arrive
                agent.BB.AddToBlacklist(agent.BB.TargetTile.Id);
                agent.BB.TargetTile = null;
                // try another one next iteration
                return InProgress;
            }

            if (agent.BB.TargetTile.TryGetComponent<ObjStorageComp>(out var storage)) {
                agent.Being.Graphics.Drop(objData.Obj);
                if (!storage.Store(agent, objData, goal)) {
                    return Failed(T.StorageUnavailable);
                }
            }

            goal.WorldState.Set(AIVarsH.IsObjStored, true);
            TryWalkAway(agent);
            return Success;
        }

        private AIActionResult ReserveObj(AIAgentComp agent, AIGoal goal, ref ObjData objData) {
            if (goal.Target is StorableObjComp sobj) {
                if (sobj.Obj.Data.TryReserve(goal)) {
                    objData = sobj.Obj.Data;
                    agent.BB.TargetObjData = objData;
                    return InProgress;
                } else {
                    // failed to reserve
                    return Failed(T.AdRejCouldNotReserve);
                }
            } else {
                return Error("Store obj target is not StorableObjComp: {0}",
                    goal.Target);
            }
        }

        private AIActionResult PickUpObj(AIAgentComp agent, ObjData objData, int agentPos) {
            if (objData.Obj == null) {
                D.Err("ObjData.Obj is null when trying to pick up! {0}", objData);
                return Failed(T.AdRejTargetUnavailable);
            }
            // should be near, but let's check
            var objPos = objData.Obj.PosIdx;
            var dist = agent.BB.MoveProgress == null ? 1 : 4;
            if (Pos.SqrDistance(objPos, agentPos) > dist) {
                if (agent.BB.MoveProgress != null) {
                    // we already tried this before, let's fail
                    // just reserved, let's go there
                    return FailReachObj(agent, objPos);
                }
                agent.BB.MoveTargetPos = objPos;
                if (agent.Motion.TryGoTo(objPos,
                        PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                        out agent.BB.MoveProgress)) {
                    return InProgress;
                } else {
                    return FailReachObj(agent, objPos);
                }
            }
            if (agent.Being.Graphics.Carry(objData.Obj, true, false)) {
                agent.BB.Bool1NotSaved = true;
            } else {
                return Error("Failed to carry obj {0} {1}",
                    objData.Obj, agent);
            }
            return InProgress;
        }

        private AIActionResult FailReachObj(AIAgentComp agent, int objPos) {
            if (agent.Motion.IsGrounded) {
                agent.Motion.StoreNavFailureAt(objPos);
                return FailedSoft(T.AdRejUnreachableItem);
            }
            return FailedNavToTarget(agent);
        }

        private AIActionResult FindStorage(GameState S, AIAgentComp agent,
                ObjData objData, int agentPos, AIGoal goal, long ticks) {
            // find the storage to go to
            if (objData.Obj == null || objData.Obj.StorableObj == null) {
                D.Err("Trying to find storage for an objData that doesn't have an obj or a StorableObj comp! {0} | {1}",
                    objData, objData.Obj);
                return Failed(T.OperationFailed);
            }

            var isG = agent.Motion.IsGrounded;
            int agentIslandId = 0;
            if (isG) {
                agentIslandId = S.Sys.Areas.IslandAt(agentPos);
            }

            var storages = S.Query.ObjStoragesForGroup.Ask(
                objData.Obj.StorableObj.GroupH);
            if (Arrays.IsEmpty(storages)) {
                return Failed(T.StorageUnavailable);
            }
            ObjStorageComp closest = null;
            int bestPriority = 0;
            float bestDist = float.MaxValue;
            // True when at least one storage was skipped because it was unsafe or
            // on an unreachable island for this specific worker (but not because it
            // was full/rejected — that's a global condition, not worker-specific).
            bool existsButUnreachable = false;
            for (int i = storages.Count - 1; i >= 0; i--) {
                ObjStorageComp s = storages[i];
                if (s.HaulTarget?.HaulPriority == 0) {
                    continue;
                }
                if (agent.BB.IsBlacklisted(s.EntityId)) {
                    continue;
                }
                if (s.IsFull || (s.HaulTarget != null
                        && !s.HaulTarget.Accepts(objData))) {
                    agent.BB.AddToBlacklist(s.EntityId);
                    continue;
                }
                var sPos = s.InPort;
                if (!agent.IsFeelingSafeAt(sPos)) {
                    agent.BB.AddToBlacklist(s.EntityId);
                    existsButUnreachable = true;
                    continue;
                }
                if (isG) {
                    if (!S.Sys.Areas.CanReachGroundedIslandToPos(
                            agentIslandId, sPos)) {
                        agent.BB.AddToBlacklist(s.EntityId);
                        existsButUnreachable = true;
                        continue;
                    }
                }
                int priority = s.HaulTarget?.HaulPriority ?? 5;
                float dist = Pos.SqrDistance(agentPos, sPos);
                if (EntityUtils.IsBetterStorage(priority, dist, bestPriority, bestDist)) {
                    closest = s;
                    bestPriority = priority;
                    bestDist = dist;
                }
            }
            if (closest == null) {
                if (existsButUnreachable) {
                    // A storage exists but this worker can't reach/enter it safely.
                    // Put the goal back so another worker can try, and cool this
                    // worker down so it doesn't immediately re-win the bid and loop.
                    agent.BB.SetGoalCooldown(goal.Id, ticks,
                        Consts.TicksPer2Hours, Consts.TicksPer4Hours);
                    return FailedSoft(T.NavFailUnreachableGrounded);
                }
                return Failed(T.StorageUnavailable);
            }
            // try to go to closest
            agent.BB.MoveTargetPos = closest.InPort;
            if (!agent.Motion.TryGoTo(closest.InPort,
                    PathReqFlags.AdjOnTop | PathReqFlags.TempDestination, out agent.BB.MoveProgress)) {
                agent.Motion.StoreNavFailureAt(closest.InPort);
                agent.BB.AddToBlacklist(closest.EntityId);
                // try next iteration
                return InProgress;
            }
            // memorize the storage we are going towards
            agent.BB.TargetTile = closest.Tile;
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 3f;
        }
    }
}
