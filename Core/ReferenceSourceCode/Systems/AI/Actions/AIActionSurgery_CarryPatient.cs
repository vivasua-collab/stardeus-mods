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
using Game.Systems.Creatures;
using Game.Systems.Creatures.Surgeries;

namespace Game.Systems.AI {
    public sealed class AIActionSurgery_CarryPatient : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionSurgery_CarryPatient());
        }
        public const string ActId = "Surgery_CarryPatient";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionSurgery_CarryPatient() : base(ActId, () => T.CarryingBeing) {
            Preconditions = AIState.With(AIVarsH.IsNear, true)
                    .And(AIVarsH.IsPatientDelivered, false);
            Outcomes = AIState.With(AIVarsH.IsPatientDelivered, true);
            WithRequiredJobType(JobTypeIdH.Hauling);
            WithRequiredAbilities(AbilityIdH.DumbLabor, AbilityIdH.Carry);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // picked up the patient
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            agent.BB.UnreserveSlot(false, false);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            agent.BB.UnreserveSlot(false, keepUsing: true);
            agent.Being.Graphics.DropIfCarrying();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {

            if (goal.Target.Entity is not Being patient) {
                return Error("Patient is not Being: {0}", goal);
            }

            if (agent.Motion.IsBusy) {
                if (!agent.BB.Bool1NotSaved) {
                    // grab asap if we can reach
                    if (Pos.AreAdjacent(agent.PosIdx, patient.PosIdx)) {
                        agent.Motion.Abort(T.LocationNearby);
                        if (!agent.Being.Graphics.Carry(patient)) {
                            D.Err("Failed to pick up the patient: {0} -> {1} -> {2}",
                                agent, goal, goal.Target);
                            return Failed(T.AdRejTargetUnavailable);
                        }
                        agent.BB.Bool1NotSaved = true;
                    }
                }
                return InProgress;
            }

            if (!agent.BB.Bool1NotSaved) {
                if (!S.Query.HasPendingSurgeries.Ask(patient.Data, null)) {
                    D.Warn("Patient doesn't have any pending surgeries, cancelling carry task: {0} -> {1}",
                        goal, patient);
                    AIGoal.Cancel(goal, ticks, T.AdRejCancelled);
                    return InProgress;
                }
                if (!Pos.AreAdjacent(agent.PosIdx, patient.PosIdx)) {
                    // patient moved!
                    D.Warn("Patient is escaping, let's try to carry again! {0} | {1}",
                        Pos.StringFromTo(agent.PosIdx, patient.PosIdx), patient);
                    goal.WithWorld(AIVarsH.IsNear, false);
                    return NeedsReplan(UnreachableBy(agent));
                }
                if (!agent.Being.Graphics.Carry(patient)) {
                    D.Err("Failed to pick up the patient: {0} -> {1} -> {2}",
                        agent, goal, goal.Target);
                    return Failed(T.AdRejTargetUnavailable);
                }
                agent.BB.Bool1NotSaved = true;
            }

            SurgicalOpType opType = BodyModSpotComp.GetOpType(goal, out bool needsPart, out bool needsBodySlot);
            if (opType == SurgicalOpType.Undefined) {
                return Error("Operation Type not defined in surgery goal: {0}", goal);
            }

            int designationId;

            // also possibly try to get the part
            uint objId = 0;
            ObjData data = null;

            if (needsPart) {
                if (!goal.TryGetVar(AIGoalVars.ObjId_Uint, out objId)) {
                    return Error("Body part id var is not set for surgery goal: {0}", goal);
                }
                // we won't carry the body part but we need to know the type
                // in order to find the correct designation of the operating table
                data = S.Query.FindObjData.Ask(objId);
                if (data == null) {
                    D.Err("Body part not available: {0}", goal);
                    return Failed(T.AdRejTargetUnavailable);
                }
            }

            if (patient.Species.IsBiological) {
                designationId = SlotDesignationH.BodyModSpot_Organic;
            } else {
                designationId = SlotDesignationH.BodyModSpot_Robotic;
            }

            if (agent.BB.ReservedSlot == null) {
                return TryFindSlot(S, agent, goal, patient.Data, ticks, designationId);
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

            agent.Being.Graphics.Drop(patient);
            if (agent.BB.ReservedSlot.Parent is BodyModSpotComp modSpot) {
                string bodyPartSlotId = null;
                if (needsBodySlot && !goal.TryGetVar(AIGoalVars.BodySlotId_String, out bodyPartSlotId)) {
                    D.Err("Could not get target body part slot when loading patient: {0} / {1}",
                        goal, agent);
                    return Error("Misconfigured goal, no body part slot id var");
                }
                // must unreserve the slot and re-reserve it for the patient
                agent.BB.UnreserveSlot(false, false);
                if (modSpot.LoadPatient(patient, opType, bodyPartSlotId, goal, false)) {
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

        private AIActionResult TryFindSlot(GameState S, AIAgentComp agent, AIGoal goal,
                BeingData patient,
                long ticks, int designationId) {
            var slot = S.Sys.Slots.FindForDesignationOnBehalfOf<BodyModSpotComp>(
                    agent.BeingData, patient,
                    designationId, agent.BB.Blacklist, null, out var target);

            if (slot == null) {
                if (Arrays.IsEmpty(agent.BB.Blacklist)) {
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
                    agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer15Minutes,
                        Consts.TicksPer30Minutes);
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
                // cannot do this for ourselves, there a Surgery_GoTo action for it
                return 0f;
            }
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 3f;
        }
    }
}
