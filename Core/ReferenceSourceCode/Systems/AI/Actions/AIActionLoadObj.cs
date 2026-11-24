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
using Game.Systems.BodyParts;
using Game.Systems.Items;

namespace Game.Systems.AI {
    public sealed class AIActionLoadObj : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionLoadObj());
        }
        public const string ActId = "AIActionLoadObj";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionLoadObj() : base(ActId, () => T.CarryingObj) {
            Preconditions = AIState.With(AIVarsH.IsObjLoaded, false);
            Outcomes = AIState.With(AIVarsH.IsObjLoaded, true);
            WithRequiredAbilities(AbilityIdH.DumbLabor, AbilityIdH.Carry);
            MarkAsWork();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
            // reserved object
            agent.BB.TargetObjData = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetObjData?.TryUnreserve(goal);
            agent.BB.TargetObjData = null;
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
            agent.Being.Graphics.DropIfCarrying();
        }

        private bool IsCarryingObj(AIAgentComp agent) {
            return agent.BB.Bool1NotSaved;
        }
        private void SetCarryingObj(AIAgentComp agent) {
            agent.BB.Bool1NotSaved = true;
        }

        private AIActionResult TryGetObj(GameState S, AIAgentComp agent, ObjData data, AIGoal goal) {
            // try go to obj
            int objPosIdx;
            ObjStorageComp objStorage = null;
            PathReqFlags pathFlags = PathReqFlags.AdjOnTop | PathReqFlags.TempDestination;

            if (data.State == ObjState.Destroyed) {
                return Failed(T.AdRejTargetUnavailable);
            }
            if (data.StorageId == 0) {
                if (data.Obj == null) {
                    return Error("Both objData.Obj and objData.StorageId is not set for {0}!",
                        data);
                }
                objPosIdx = data.Obj.PosIdx;
                pathFlags |= PathReqFlags.AdjNearby;
            } else {
                objStorage = S.Components.Find<ObjStorageComp>(data.StorageId);
                if (objStorage == null) {
                    return Error("Storage is not found for obj with StorageId: {0} / {1}",
                        data.StorageId, data);
                }
                objPosIdx = objStorage.InPort;
            }

            if (agent.BB.MoveProgress != null) {
                if (Pos.SqrDistance(agent.PosIdx, objPosIdx) < 2) {
                    if (objStorage != null) {
                        // take the object out
                        if (!objStorage.TryTake(agent, data, goal)) {
                            D.Err("Agent could not take the body part from storage! {0} / {1} / {2}",
                                data, objStorage, agent);
                            return InProgress;
                        }
                    }
                    if (!agent.Being.Graphics.Carry(data.Obj, false, false)) {
                        D.Err("Failed carrying obj! {0}", data.Obj);
                        return Failed(T.AdRejTargetUnavailable);
                    }
                    SetCarryingObj(agent);
                    agent.BB.ClearMoveTarget();
                } else {
                    return Failed(T.AdRejUnreachableItem);
                }
            } else {

                agent.BB.MoveTargetPos = objPosIdx;
                if (agent.Motion.TryGoTo(objPosIdx, pathFlags,
                        out agent.BB.MoveProgress)) {
                    return InProgress;
                } else {
                    return Failed(T.AdRejUnreachableItem);
                }
            }
            return InProgress;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {

            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            ObjData data = agent.BB.TargetObjData;
            if (data == null) {
                if (!goal.TryGetVar(AIGoalVars.ObjId_Uint, out uint objId)) {
                    return Error("Body part id var is not set for surgery goal: {0}", goal);
                }
                data = S.Query.FindObjData.Ask(objId);
                if (data == null) {
                    D.Warn("Failed to find obj data by id in load obj: {0}", objId);
                    goal.Flags |= AIGoalFlags.NonRetryable;
                    return Failed(T.AdRejTargetUnavailable);
                } else {
                    // D.Warn("Found obj to load: {0}", data);
                }
                if (data.State == ObjState.InDevice) {
                    // sanity check, is it already loaded to our target device?
                    if (data.DeviceId == goal.Target.EntityId) {
                        D.Err("Device wants us to load an object which is already loaded into the device! {0}",
                            goal.Target.Entity, data);
                        // try to recover
                        if (goal.Target is ILoadsObj l) {
                            goal.WorldState.Set(AIVarsH.IsObjLoaded, true);
                            l.LoadObj(data, goal);
                        }
                        return Success;
                    }
                }
                // before we try to reach the obj, let's attempt to reserve it
                if (data.TryReserve(goal)) {
                    agent.BB.TargetObjData = data;
                } else {
                    // failed to reserve
                    return Failed(T.AdRejCouldNotReserve);
                }
            }

            if (!IsCarryingObj(agent)) {
                return TryGetObj(S, agent, data, goal);
            }

            // we have to try to carry the item to target now
            if (goal.Target is not ILoadsObj loader) {
                return Error("Goal target is not ILoadsObj! {0} ({1})",
                    goal.Target, goal.Target.Entity);
            }

            // go to goal.Target
            if (agent.BB.MoveProgress == null) {
                if (TryGoToOnce(agent, loader.LoadObjSpotIdx, false)) {
                    return InProgress;
                } else {
                    return FailedNavToTarget(agent, loader.LoadObjSpotIdx);
                }
            }

            // should be near
            if (agent.PosIdx != loader.LoadObjSpotIdx) {
                // we failed to reach the load spot
                return FailedNavToTarget(agent);
            }

            if (loader.LoadObj(data, goal)) {
                goal.WorldState.Set(AIVarsH.IsObjLoaded, true);
                return Success;
            }

            D.Err("Failed loading obj into device: {0} -> {1}, {2}",
                data, loader, goal);
            return Failed(T.AdRejTargetUnavailable);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 3f;
        }
    }
}
