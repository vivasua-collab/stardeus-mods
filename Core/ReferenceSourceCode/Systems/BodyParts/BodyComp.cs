using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.Items;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.BodyParts {

    public sealed class BodyComp : BaseComponent<BodyComp>,
            IAIGoalProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BodyComp());
        }

        public bool IsReachableForCommonAction {
            get {
                if (HasAnyGoal) { return false; }
                var d = Being.Data;
                if (d.HasStateFlag(BeingState.Alive)) {
                    if (!d.Body.IsUnhealthy) {
                        // alive and well
                        return false;
                    }
                    if (!d.IsPlayer && !d.Body.IsIncapacitated) {
                        // alive non-incapacitated enemy
                        return false;
                    }
                }
                if (!d.Body.HasSurgeryFacilities(out _)) {
                    return false;
                }
                return true;
            }
        }

        public bool HasExistingGoal(bool showWarning, out string failReason) {
            AIGoal existing = null;
            failReason = null;
            if (HasGoal) {
                existing = entity.Goal;
            }
            if (existing == null && Being.Data.HasStateFlag(BeingState.InDevice)) {
                var device = EntityUtils.ParentObjAt(Pos.FromVector(Being.Position));
                if (device != null && device.TryGetComponent<BodyModSpotComp>(out var modSpot)) {
                    if (device.Goal != null) {
                        failReason = T.EventOngoing_V1.F(modSpot.CurrentTaskName);
                        existing = device.Goal;
                    }
                }
            }
            if (existing != null) {
                failReason ??= T.EventOngoing_V1.F(
                    $"{existing.Name}: {AIGoal.StateNameSimple(existing.State)}");
                if (showWarning) {
                    S.Sig.ShowErrorMessage.Send(failReason);
                }
                return true;
            }
            return false;
        }

        public bool CanEuthanize {
            get {
                if (HasAnyGoal) { return false; }
                if (!Being.IsActive || Being.IsDead) { return false; }
                var d = Being.Data;
                if (!d.Body.IsInjured || !d.Body.IsIncapacitated) { return false; }
                if (!d.Species.IsBiological) { return false; }
                if (d.HasStateFlag(BeingState.InDevice)) { return false; }
                return true;
            }
        }

        public bool AddEuthanizeButton(QuickChoice choice, Being forceAssign = null) {
            if (!CanEuthanize) {
                return false;
            }
            choice.AddButton(T.VerbEuthanize, () => {
                if (CreateEuthanizeGoal()) {
                    if (forceAssign != null) {
                        if (!BeingUtils.TryAssignGoal(G.Ticks, entity.Goal, forceAssign.AIAgent)) {
                            S.Sig.ShowErrorMessage.Send(T.AdjIncapable);
                            AIGoal.Cancel(entity.Goal, G.Ticks);
                        }
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }
            });
            return true;
        }

        public bool CreateEuthanizeGoal() {
            if (HasAnyGoal) {
                S.Sig.ShowErrorMessage.Send(T.AnotherTaskInProgress_V1.F(
                    Texts.Accented(entity.Goal.Name)));
                return false;
            }
            var g = CreateGoal(JobTypeIdH.Task, IconId.CKnife, T.VerbEuthanize, this)
                .WithDesired(AIVarsH.IsNear, true)
                .WithWorldToDesired(AIVarsH.IsEuthanized, false, true)
                .WithPriority(A.BodyPartProcedurePriority)
                .WithFlag(AIGoalFlags.IgnoreDeadBodies)
                .WithRequiredAbilities(AbilityIdH.UseItem, AbilityIdH.DumbLabor)
                .ByPlayer(true)
                .WriteInitialState();
            if (!entity.TrySetGoal(g)) {
                UISounds.PlayActionDenied();
                return false;
            }
            return true;
        }

        private bool TryOrderBulkOperation(SurgicalOpType opType, string iconId,
                SurgicalOperation op, out string failReason) {
            if (!Being.IsActive) {
                failReason = T.NotAvailable;
                return false;
            }
            if (opType == SurgicalOpType.ReplaceIrreparableAndMissing
                    || opType == SurgicalOpType.FillMissing) {
                if (!Being.Data.Body.BodySlot.IsUsableWithParent) {
                    failReason = $"{T.CondDamaged}: {Being.Data.Body.BodySlot.NameT}";
                    return false;
                }
            }
            if (TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks);
            } else if (HasExistingGoal(false, out failReason)) {
                // some other goal exists
                return false;
            }
            if (!Being.Data.Body.HasSurgeryFacilities(out var defIdH)) {
                failReason = $"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(defIdH)}";
                return false;
            }
            var isAlive = Being.IsAlive;
            if (isAlive && !Being.Data.IsPlayer) {
                if (!Being.Data.Body.IsIncapacitated) {
                    failReason = $"{T.NounEnemy}: {T.BeingStateAlive}";
                    return false;
                }
            }

            bool canForceSelfOperate = true;
            if (Being.Data.Species.IsBiological) {
                if (Being.Data.Species.IsHumanoid && isAlive && Being.Data.IsPlayer) {
                    if (opType == SurgicalOpType.RemoveAll && !A.HasAsimovOverride) {
                        // The.Dialog.ShowSimple(IconId.CComputing, T.AsimovViolation1);
                        D.Err("Asimov violation when doing bulk op: {0} -> {1}", op, Being);
                        failReason = T.AsimovViolation1;
                        return false;
                    }
                    canForceSelfOperate = Being.Data.HasEnvFlag(BeingEnvFlags.Controllable);
                }
            }
            string goalName;
            if (Being.Data.Species.IsBiological) {
                goalName = T.PartPerformSurgery;
            } else {
                switch (opType) {
                    case SurgicalOpType.FillMissing: goalName = $"{T.Install}: {T.Missing}"; break;
                    case SurgicalOpType.ReplaceIrreparableAndMissing: goalName = $"{T.Replace}: {T.All}"; break;
                    case SurgicalOpType.Install: goalName = T.Install; break;
                    case SurgicalOpType.Swap: goalName = T.PartPerformReplacement; break;
                    default: goalName = T.PartOrderHarvest; break;
                }
            }
            g = CreateGoal(JobTypeIdH.Task, iconId, goalName, this)
                .WithVar(AIGoalVars.SurgeryType_Int, (int) opType)
                .WithVar(AIGoalVars.SurgeryId, op.Id)
                .WithOverwritePriority(2)
                .WithPriority(A.BodyPartProcedurePriority)
                .ByPlayer(true)
                .WithWorldToDesired(AIVarsH.IsPatientDelivered, false, true)
                // this goal can be done in two ways - patient can go by themselves, or someone can carry
                // move is checked already, but since we don't have WithoutRequiredAbilities on AIGoal,
                // let's check for Move again, or an error will be triggered since no required abilities
                // would be listed otherwise
                .WithRequiredAbilities(AbilityIdH.Move)
                .WriteInitialState();
            g.CheckDoable = CanPerformSurgeryNow;

            AIAgentComp forcedAgent = null;
            if (canForceSelfOperate && !Being.Motion.IsImmobilized) {
                forcedAgent = Being.AIAgent;
                if (forcedAgent.Goal != null) {
                    AIGoal.Interrupt(forcedAgent.Goal, G.Ticks, goalName);
                }
            }
            if (entity.TrySetGoal(g, forcedAgent)) {
                op.AssociatedGoal = g;
                failReason = null;
                return true;
            } else {
                D.Err("Failed setting goal after all checks! {0}", g);
                failReason = T.Error;
                return false;
            }
        }

        private AIGoalCheckResult CanPerformSurgeryNow(GameState S, AIGoal goal) {
            int designationId;
            int defIdH;
            if (Being.Species.IsBiological) {
                designationId = SlotDesignationH.BodyModSpot_Organic;
                defIdH = DefIdsH.ObjectsDevicesSurgeryHub;
            } else {
                designationId = SlotDesignationH.BodyModSpot_Robotic;
                defIdH = DefIdsH.ObjectsDevicesAssemblyHub;
            }
            if (S.Sys.Slots.HasFreeFor(designation: designationId)) {
                return AIGoalCheckResult.Doable;
            }
            // the hub exists but every slot is taken
            if (S.Sys.Slots.HasFor(designationId)) {
                return AIGoalCheckResult.Fail($"{T.AdjBusy}: {Texts.LinkToCodex(defIdH)}", true);
            }
            return AIGoalCheckResult.Fail(
                $"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(defIdH)}", true);
        }

        public bool TryOrderOp(SurgicalOperation op) {
            if (op.TargetSlot == null) {
                if (TryOrderBulkOperation(op.OpType, op.Icon, op, out var failReason)) {
                    return true;
                }
                D.Err("Failed ordering mass body part op: {0}: {1} | {2}",
                    op, failReason, Being);
                return false;
            }
            if (op.OpType == SurgicalOpType.Remove) {
                return TryOrderRemove(op.TargetSlot, op);
            }
            if (op.ReservedPart == null) {
                D.Err("SurgicalOp of type {0} doesn't have a reserved part! Failing: {1}", op.OpType, op);
                op.State.Set(SurgicalOpState.Failed);
                return false;
            }
            if (op.ReservedPart.State == ObjState.Destroyed) {
                // it will never come back, retrying would loop forever (STA-1584)
                op.FailReason = T.AdRejTargetUnavailable;
                op.State.Set(SurgicalOpState.Failed);
                return false;
            }
            // ordered before such installs were refused: nothing below that slot can work, so no
            // part is hauled for it (STA-1749)
            var deadEnd = op.TargetSlot?.BeyondRepairAncestor;
            if (deadEnd != null) {
                op.FailReason = T.BodyPartUnusableViaParent_V1.F(deadEnd.NameT);
                op.State.Set(SurgicalOpState.Failed);
                return false;
            }
            if (op.ReservedPart.Reservation == null) {
                if (!op.ReservedPart.TryReserveForOperation(op)) {
                    D.Err("SurgicalOp is linked to part that didn't have a reservation, and failed to reserve! {0}->{1}",
                        op, op.ReservedPart.Reservation);
                }
            }
            if (op.ReservedPart.Reservation?.Surgery != op) {
                D.Err("SurgicalOp is linked to part that doesn't have the right reservation! {0}->{1}",
                    op, op.ReservedPart.Reservation);
                return false;
            }
            if (TryOrderInstall(op.TargetSlot, op.ReservedPart, op, out var failReason1)) {
                return true;
            }
            D.Err("Failed ordering mass body install op: {0}: {1} | {2}",
                op, failReason1, Being);
            return false;
        }

        private bool TryOrderRemove(BodyPartSlot slot, SurgicalOperation op) {
            if (!Being.IsActive) { return false; }
            if (slot.IsPartRemoved) { return false; }
            if (HasExistingGoal(false, out _)) { return false; }
            if (!slot.Body.HasSurgeryFacilities(out var defIdH)) {
                // S.Sig.ShowErrorMessage.Send($"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(defIdH)}");
                return false;
            }
            var isAlive = Being.IsAlive;
            if (isAlive && !Being.Data.IsPlayer) {
                if (!Being.Data.Body.IsIncapacitated) {
                    return false;
                }
            }
            bool canForceSelfOperate = true;
            if (Being.Data.Species.IsBiological) {
                if (isAlive && Being.Data.Species.IsHumanoid && Being.Data.IsPlayer) {
                    if (!slot.Type.IsOptional && !slot.State.IsEthicallyRemovable && !A.HasAsimovOverride) {
                        // The.Dialog.ShowSimple(IconId.CComputing, T.AsimovViolation1);
                        D.Err("Asimov violation in order remove! {0}->{1}", op, Being);
                        return false;
                    }
                }
                canForceSelfOperate = Being.Data.HasEnvFlag(BeingEnvFlags.Controllable)
                    && !Being.Data.Species.IsAnimal;
            }
            string iconId = IconId.CBodyRemoveOne;
            var g = CreateGoal(JobTypeIdH.Task, iconId, T.PartOrderHarvest, this)
                .WithVar(AIGoalVars.BodySlotId_String, slot.Id)
                .WithVar(AIGoalVars.SurgeryType_Int, (int) SurgicalOpType.Remove)
                .WithVar(AIGoalVars.SurgeryId, op.Id)
                .WithWorldToDesired(AIVarsH.IsPatientDelivered, false, true)
                .WithOverwritePriority(1)
                .WithPriority(A.BodyPartProcedurePriority)
                .WithRequiredAbilities(AbilityIdH.Manipulate, AbilityIdH.Intelligence)
                .ByPlayer(true)
                .WriteInitialState();
            g.CheckDoable = CanPerformSurgeryNow;

            AIAgentComp forcedAgent = null;
            if (canForceSelfOperate && !Being.Motion.IsImmobilized) {
                forcedAgent = Being.AIAgent;
            }
            if (entity.TrySetGoal(g, forcedAgent)) {
                op.AssociatedGoal = g;
                return true;
            }
            return false;
        }

        private bool TryOrderInstall(BodyPartSlot slot, ObjData data,
                SurgicalOperation op, out string failReason) {
            if (slot == null) {
                D.Err("Trying to install part into a null slot! {0}. {1}",
                    data, Being);
                failReason = T.Error;
                return false;
            }
            if (!Being.IsActive) {
                failReason = T.NotAvailable;
                return false;
            }

            int designationId;
            int defIdH;
            if (Being.Data.Species.IsBiological) {
                designationId = SlotDesignationH.BodyModSpot_Organic;
                defIdH = DefIdsH.ObjectsDevicesSurgeryHub;
            } else {
                designationId = SlotDesignationH.BodyModSpot_Robotic;
                defIdH = DefIdsH.ObjectsDevicesAssemblyHub;
            }
            if (!S.Sys.Slots.HasCompatibleFor(designationId, Being.Data)) {
                failReason = $"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(defIdH)}";
                return false;
            }

            data.Validate(S);
            if (!data.IsAvailableIgnoreReservation) {
                D.Err("Trying to order install of a part that is not available! {0}",
                    data);
                failReason = T.AdRejTargetUnavailable;
                return false;
            }
            if (HasExistingGoal(false, out failReason)) {
                return false;
            }
            string iconId;
            string desc;
            SurgicalOpType opType;
            if (slot.IsPartRemoved) {
                opType = SurgicalOpType.Install;
                iconId = IconId.CBodyInstallOne;
            } else {
                opType = SurgicalOpType.Swap;
                iconId = IconId.CBodyReplaceBad;
            }
            if (Being.Data.Species.IsBiological) {
                desc = T.PartPerformSurgery;
            } else {
                desc = T.PartPerformReplacement;
            }
            if (data.Obj != null) {
                // unstored obj!
                data.Obj.StorableObj.IsForbidden = true;
                if (data.Obj.Goal != null) {
                    AIGoal.Cancel(data.Obj.Goal, G.Ticks, T.MaterialReserved_V1.F(
                        T.NounSurgery));
                }
            }
            var g = CreateGoal(JobTypeIdH.Task, iconId, desc, this)
                .WithVar(AIGoalVars.ObjId_Uint, data.Id)
                .WithVar(AIGoalVars.BodySlotId_String, slot.Id)
                .WithVar(AIGoalVars.SurgeryType_Int, (int) opType)
                .WithVar(AIGoalVars.SurgeryId, op.Id)
                .WithWorldToDesired(AIVarsH.IsPatientDelivered, false, true)
                .WithOverwritePriority(1)
                .WithPriority(A.BodyPartProcedurePriority)
                // this goal can be done in two ways - patient can go by themselves, or someone can carry
                // move is checked already, but since we don't have WithoutRequiredAbilities on AIGoal,
                // let's check for Move again, or an error will be triggered since no required abilities
                // would be listed otherwise
                .WithRequiredAbilities(AbilityIdH.Move)
                .ByPlayer(true)
                .WriteInitialState();
            g.CheckDoable = CanPerformSurgeryNow;
            if (!data.TryReserve(g)) {
                failReason = T.AdRejCouldNotReserve;
                return false;
            }
            AIAgentComp forcedAgent = null;
            if (!Being.Motion.IsImmobilized) {
                if (g.CanBeDoneBy(Being.AIAgent)) {
                    forcedAgent = Being.AIAgent;
                }
            }
            if (entity.TrySetGoal(g, forcedAgent)) {
                op.AssociatedGoal = g;
                failReason = null;
                return true;
            }
            D.Err("Failed to set rescue goal: {0}", g);
            failReason = T.AdRejUndefined;
            return false;
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
        }


        public void OnGoalLoad(AIGoal goal) {
            if (goal.Name != T.VerbEuthanize) {
                goal.CheckDoable = CanPerformSurgeryNow;
            }
        }
    }
}
