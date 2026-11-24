using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Systems.Path;
using KL.Grid;
using Game.Systems.Items;
using Game.Systems.Creatures.Surgeries;

namespace Game.Systems.AI {
    public sealed class AIActionSurgery_GoTo : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionSurgery_GoTo());
        }
        public const string ActId = "Surgery_GoTo";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionSurgery_GoTo() : base(ActId, () => T.VerbGoingTo_V1.F(T.NounSurgery)) {
            Preconditions = AIState.With(AIVarsH.IsPatientDelivered, false);
            Outcomes = AIState.With(AIVarsH.IsPatientDelivered, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsRest();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // tried getting part
            agent.BB.Bool1 = false;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            agent.BB.UnreserveSlot(false, false);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            agent.BB.UnreserveSlot(false, keepUsing: true);
            agent.Being.Graphics.DropIfCarrying();
        }

        private bool TriedGettingPart(AIAgentComp agent) {
            return agent.BB.Bool1;
        }
        private void SetTriedGettingPart(AIAgentComp agent) {
            agent.BB.Bool1 = true;
        }

        private AIActionResult TryGetPart(GameState S, AIAgentComp agent,
                ObjData data, AIGoal goal) {
            if (!agent.BeingData.HasAbility(AbilityIdH.Carry)) {
                SetTriedGettingPart(agent);
                return InProgress;
            }

            // try go to part
            int partPosIdx;
            ObjStorageComp partStorage = null;

            if (data.StorageId == 0) {
                if (data.State == ObjState.Destroyed) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                if (data.Obj == null) {
                    return Error("Both objData.Obj and objData.StorageId is not set for {0}!",
                        data);
                }
                partPosIdx = data.Obj.PosIdx;
            } else {
                partStorage = S.Components.Find<ObjStorageComp>(data.StorageId);
                if (partStorage == null) {
                    return Error("Storage is not found for obj with StorageId: {0} / {1}",
                        data.StorageId, data);
                }
                partPosIdx = partStorage.InPort;
            }

            if (agent.BB.MoveProgress != null) {
                SetTriedGettingPart(agent);
                // we should be near the part!
                if (Pos.SqrDistance(agent.PosIdx, partPosIdx) < 2) {
                    if (partStorage != null) {
                        // take the object out
                        if (!partStorage.TryTake(agent, data, goal)) {
                            D.Err("Agent could not take the body part from storage! {0} / {1} / {2}",
                                data, partStorage, agent);
                            return InProgress;
                        }
                    }
                    if (!agent.Being.Graphics.Carry(data.Obj, false, false)) {
                        D.Err("Could not carry part: {0} | {1}", data.Obj, goal);
                    }
                }
            } else {
                agent.BB.MoveTargetPos = partPosIdx;
                if (agent.Motion.TryGoTo(partPosIdx,
                        PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination,
                        out agent.BB.MoveProgress)) {
                    return InProgress;
                } else {
                    SetTriedGettingPart(agent);
                }
            }
            return InProgress;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {

            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            var opType = BodyModSpotComp.GetOpType(goal, out bool needsPart, out bool needsBodySlot);
            if (opType == SurgicalOpType.Undefined) {
                return Error("Operation Type not defined in surgery goal: {0}", goal);
            }

            int designationId;
            ObjData data = null;

            // also possibly try to get the part
            uint objId = 0;

            if (needsPart) {
                if (!goal.TryGetVar(AIGoalVars.ObjId_Uint, out objId)) {
                    return Error("Body part id var is not set for surgery goal: {0}", goal);
                }
                data = S.Query.FindObjData.Ask(objId);
                if (data == null) {
                    // this goal can never be completed
                    AIGoal.Cancel(goal, ticks, T.AdRejTargetUnavailable);
                    return InProgress;
                    // return Failed(T.AdRejTargetUnavailable);
                }
                if (!TriedGettingPart(agent)) {
                    return TryGetPart(S, agent, data, goal);
                }
            }

            if (agent.BeingData.Species.IsBiological) {
                designationId = SlotDesignationH.BodyModSpot_Organic;
            } else {
                designationId = SlotDesignationH.BodyModSpot_Robotic;
            }

            if (agent.BB.ReservedSlot == null) {
                return TryFindSlot(S, agent, goal, ticks, designationId);
            } else {
                // we should be near the slot
                var slotPos = agent.BB.ReservedSlot.SlotPosIdx;
                if (slotPos != agent.PosIdx) {
                    // we failed?
                    D.Warn("Failed to reach surgery spot: {0} -> {1}",
                        agent.Being, agent.BB.ReservedSlot.Parent.Entity);
                    agent.Motion.StoreNavFailureAt(slotPos, agent.PosIdx);
                    agent.BB.UnreserveSlot(true, false);
                    return InProgress;
                }
            }

            if (agent.BB.ReservedSlot.Parent is BodyModSpotComp modSpot) {
                bool isPartDelivered = agent.Being.Graphics.IsCarrying();
                if (isPartDelivered) {
                    agent.Being.Graphics.Drop(data.Obj);
                }
                string bodyPartSlotId = null;
                if (needsBodySlot) {
                    if (!goal.TryGetVar(AIGoalVars.BodySlotId_String, out bodyPartSlotId)) {
                        D.Err("Could not get target body part slot when loading patient: {0} / {1}",
                            goal, agent);
                        return Error("Misconfigured goal, no body part slot id var");
                    }
                }
                if (modSpot.LoadPatient(agent.Being, opType, bodyPartSlotId, goal, false)) {
                    if (isPartDelivered) {
                        var loadedBodyPart = modSpot.LoadObj(data, goal);
                        if (!loadedBodyPart) {
                            D.Err("Failed loading body part into surgery device: {0} / {1}",
                                data, modSpot.Tile);
                        }
                    }

                    // find the surgery spot and go there
                    goal.WorldState.Set(AIVarsH.IsPatientDelivered, true);

                    // create the follow up goal to deliver the body part or to
                    // perform the rest of the surgery
                    modSpot.TryStartSurgery(opType, data, goal);
                    return Success;
                } else {
                    return Failed("Failed to load the patient!");
                }
            } else {
                return Error("Reserved slot is not BodyModSpotComp! {0}",
                    agent.BB.ReservedSlot);
            }
        }

        private AIActionResult TryFindSlot(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks, int designationId) {
            var slot = S.Sys.Slots.FindForDesignation<BodyModSpotComp>(
                    agent.BeingData,
                    designationId, agent.BB.Blacklist, null, out var target);

            if (slot == null) {
                if (Arrays.IsEmpty(agent.BB.Blacklist)) {
                    // no operating device
                    int deviceDefIdH = 0;
                    switch (designationId) {
                        case SlotDesignationH.BodyModSpot_Organic: {
                            deviceDefIdH = DefIdsH.ObjectsDevicesSurgeryHub;
                            break;
                        }
                        case SlotDesignationH.BodyModSpot_Robotic: {
                            deviceDefIdH = DefIdsH.ObjectsDevicesAssemblyHub;
                            break;
                        }
                        default: {
                            D.Err("Unhandled designation: {0}",
                                SlotDesignationH.ResolveName(designationId));
                            break;
                        }
                    }
                    if (deviceDefIdH == 0) {
                        return Failed(T.RequiredDeviceIsNotAvailable);
                    }
                    // no operating device that is available right now
                    if (S.Sys.Slots.HasFor(designationId)) {
                        return Failed($"{T.AdjBusy}: {Texts.LinkToCodex(deviceDefIdH)}");
                    } else {
                        return Failed($"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(deviceDefIdH)}");
                    }
                } else {
                    agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer5Minutes,
                        Consts.TicksPer15Minutes);
                    return FailedSoft(UnreachableBy(agent));
                }
            } else {
                // check if we are near the slot
                if (agent.BB.TryReserveSlot(slot, goal)) {
                    if (!agent.Motion.TryGoTo(slot.SlotPosIdx,
                            PathReqFlags.AdjOnTop | PathReqFlags.TempDestination, out agent.BB.MoveProgress)) {
                        agent.BB.AddToBlacklist(target.EntityId);
                    }
                } else {
                    agent.BB.AddToBlacklist(target.EntityId);
                }
            }
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target.Entity == agent.Being) {
                if (agent.Motion.IsImmobilized) {
                    return 0f;
                }
                if (agent.BB.IsCoolingDown(IdH, ticks)) {
                    return 0f;
                }
                return 1f;
            }
            return 0f;
        }
    }
}
