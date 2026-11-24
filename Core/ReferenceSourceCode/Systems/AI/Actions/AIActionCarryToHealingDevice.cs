using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using Game.Systems.Sleep;
using Game.Systems.Creatures;

namespace Game.Systems.AI {
    public sealed class AIActionCarryToHealingDevice : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionCarryToHealingDevice());
        }
        public const string ActId = "CarryToHealingDevice";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionCarryToHealingDevice() : base(ActId, () => T.CarryingBeing) {
            Preconditions = AIState.With(AIVarsH.IsCarriedToHealingDevice, false)
                .And(AIVarsH.IsNear, true);
            Outcomes = AIState.With(AIVarsH.IsCarriedToHealingDevice, true);
            WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // bed slot
            agent.BB.Object1 = null;
            // patient
            agent.BB.TargetBeing = null;
            agent.BB.ClearBlacklist();
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Object1 = null;
            agent.BB.UnreserveSlot(false, false);
            agent.Being.Graphics.DropIfCarrying();
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
            agent.BB.ClearBlacklist();
            // agent.BB.UnreserveSlot(false, false);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }
            if (agent.BB.TargetBeing == null) {
                if (goal.Target is ConditionsComp comp) {
                    if (comp.Being == null) {
                        return Error("Being cannot be null in ConditionsComp!");
                    }
                    agent.BB.TargetBeing = comp.Being;
                }
            }
            var patient = agent.BB.TargetBeing;
            var patientData = patient.Data;
            if (patientData.Being != patient) {
                return Failed(T.AdRejTargetUnavailable);
            }
            // The patient can get up and move while the rescuer walks over, and IsNear is
            // from the plan, so walk to where they are now (Entity.SetCarrier's reach)
            if (agent.Being.Graphics.Carrying != patient
                    && Pos.SqrDistance(agent.PosIdx, patient.PosIdx) > 4) {
                goal.WithWorld(AIVarsH.IsNear, false);
                return NeedsReplan(UnreachableBy(agent));
            }
            var agentData = agent.BeingData;
            var isBio = patient.Species.IsBiological;
            int slotDesignationH = isBio ?
                SlotDesignationH.Healing : SlotDesignationH.BotRepair;

            if (agent.BB.Object1 is BedSlot bedSlot)  {
                if (!bedSlot.Comp.Tile.IsActive) {
                    return Failed(T.AdRejTargetUnavailable);
                }
                // we should be nearby
                if (Pos.SqrDistance(agent.PosIdx, bedSlot.InPort) > 2) {
                    return FailedNavToTarget(agent);
                }
                AINeedHealth healthNeed = (AINeedHealth)
                    Registries.AINeeds.FastGet(NeedIdH.Health);
                agent.Being.Graphics.DropIfCarrying();
                if (!healthNeed.ForceStartHealing(patient, bedSlot, ticks)) {
                    return Failed(T.AdRejLackingHealer);
                }
                goal.WithWorld(AIVarsH.IsCarriedToHealingDevice, true);
                return Success;
            }

            // A bedridden patient with nothing to heal would be let straight out of a
            // healing device and rescued again forever, so only a bed helps them
            bool needsHealingDevice = !isBio || patientData.Body.CanHeal
                || patientData.Body.IsIncapacitated;
            if (agent.BB.ReservedSlot == null && needsHealingDevice) {
                // 1. is there something that we can carry the being to?
                Slot slot = null;
                if (!isBio) {
                    // prefer a station without another bot lying on it (STA-1577)
                    slot = S.Sys.Slots.FindForDesignationOnBehalfOf(
                        agentData, patientData, slotDesignationH,
                        agent.BB.Blacklist, RepairStationComp.IsUnobstructed, out _);
                }
                slot ??= S.Sys.Slots.FindForDesignationOnBehalfOf<ISlots>(
                    agentData, patientData, slotDesignationH,
                    agent.BB.Blacklist, null, out _);

                if (slot != null) {
                    if (!agent.BB.TryReserveSlot(slot, goal)) {
                        agent.BB.AddToBlacklist(slot.Parent.EntityId);
                        return InProgress;
                    }
                    if (agent.Being.Graphics.Carrying != patient
                            && !agent.Being.Graphics.Carry(patient)) {
                        agent.BB.UnreserveSlot(false, false);
                        // someone had a non-interruptable task, it's ok
                        return Failed(T.AdRejInterrupted);
                    }
                    if (!TryGoToOnce(agent, slot.SlotPosIdx, false)) {
                        agent.BB.UnreserveSlot(false, false);
                        agent.BB.AddToBlacklist(slot.Parent.EntityId);
                        agent.BB.ClearMoveTarget();
                        return InProgress;
                    }
                    return InProgress;
                }
            } else if (agent.BB.ReservedSlot != null) {
                // we should be near the slot
                if (Pos.SqrDistance(agent.PosIdx, agent.BB.ReservedSlot.SlotPosIdx) > 2) {
                    return FailedNavToTarget(agent,
                        agent.BB.ReservedSlot.Parent.Tile.PosIdx);
                }
                agent.Being.Graphics.DropIfCarrying();
                var slot = agent.BB.ReservedSlot;
                agent.BB.UnreserveSlot(false, false);
                if (isBio) {
                    AINeedHealth healthNeed = (AINeedHealth)
                        Registries.AINeeds.FastGet(NeedIdH.Health);
                    if (!healthNeed.ForceStartHealing(goal, patient, slot, ticks)) {
                        return Failed(T.AdRejLackingHealer);
                    }
                } else {
                    AINeedMaintenance maintNeed = (AINeedMaintenance)
                        Registries.AINeeds.FastGet(NeedIdH.Maintenance);
                    if (!maintNeed.ForceStartMaintenance(goal, patient,
                            slot, ticks)) {
                        return Failed($"{T.RequiredDeviceIsNotAvailable}: {slot.Parent.Tile.Definition.CodexLink}");
                    }

                }

                goal.WithWorld(AIVarsH.IsCarriedToHealingDevice, true);
                return Success;
            }

            // we don't have a slot
            if (isBio) {
                // try to find a bed
                var bed = S.Query.FindBed.Ask(patient.Data, true);
                if (bed != null) {
                    bedSlot = bed.SlotFor(patient.Data);
                    agent.BB.Object1 = bedSlot;
                    if (!TryGoToOnce(agent, bedSlot.InPort, false)) {
                        return FailedNavToTarget(agent);
                    }
                    if (!agent.Being.Graphics.Carry(patient)) {
                        return Error("Failed to carry being");
                    }
                    return InProgress;
                }
            }

            return CarryToSafety(S, agent, goal, patient);
        }

        // Nowhere to heal. A downed colonist left in vacuum or freezing cold dies waiting
        // for a bed (STA-1393), so at least get them to the nearest tile that is safe for
        // them. Decided from whether we are already carrying the patient rather than from a
        // stored phase, so it resumes correctly after a save/load.
        private AIActionResult CarryToSafety(GameState S, AIAgentComp agent, AIGoal goal,
                Being patient) {
            var patientData = patient.Data;
            if (agent.Being.Graphics.Carrying == patient) {
                if (!patientData.Safety.IsActuallySafeAt(agent.PosIdx)) {
                    return FailedNavToTarget(agent);
                }
                agent.Being.Graphics.DropIfCarrying();
                goal.WithWorld(AIVarsH.IsCarriedToHealingDevice, true);
                return Success;
            }
            if (patientData.Safety.IsActuallySafeAt(patient.PosIdx)) {
                // Out of danger already, only a healing device or a bed would help
                return Failed(LackingHealerReason(patientData));
            }
            var safePos = AIActionFindSafety.FindSafePosFor(S, patientData, agent, patient.PosIdx,
                allowSaferFallback: false);
            if (safePos == Pos.Invalid) {
                return Failed(LackingHealerReason(patientData));
            }
            if (!TryGoToOnce(agent, safePos, false)) {
                return FailedNavToTarget(agent);
            }
            if (!agent.Being.Graphics.Carry(patient)) {
                return Error("Failed to carry being");
            }
            return InProgress;
        }

        // Names the device to build: a new player whose downed bot could not be repaired
        // only saw "Lacking healing device" and did not know what was missing. A bedridden
        // colonist with nothing to heal only needs a bed, which an AutoDoc link would hide.
        public static string LackingHealerReason(BeingData patient) {
            if (!patient.Species.IsBiological) {
                return $"{T.AdRejLackingHealer}: {Texts.LinkToCodex(DefIdsH.ObjectsDevicesRepairStation)}";
            }
            if (patient.Body.CanHeal || patient.Body.IsIncapacitated) {
                return $"{T.AdRejLackingHealer}: {Texts.LinkToCodex(DefIdsH.ObjectsDevicesAutoDoc)}";
            }
            return T.AdRejLackingHealer;
        }

        public static bool IsHealingDeviceAvailableFor(GameState S, BeingData data) {
            var isBio = data.Species.IsBiological;
            int slotDesignationH = isBio ?
                SlotDesignationH.Healing : SlotDesignationH.BotRepair;
            if (S.Sys.Slots.HasCompatibleFor(slotDesignationH, data)) {
                return true;
            }
            if (isBio) {
                // see if we have a bed
                return S.Components.CountByType<BedComp>() > 0;
            }
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target.Entity == agent.Being) {
                // cannot move yourself
                return 0f;
            }
            return agent.Motion.GoToCost(goal.Target.Entity.PosIdx) * 2f;
        }
    }
}
