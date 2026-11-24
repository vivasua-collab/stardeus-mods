using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using Game.Systems.Items;
using Game.Systems.AI;
using Game.Components.Upgrades;
using Game.Systems.Creatures;
using Game.Systems.Stats;
using Game.Systems.BodyParts;
using KL.Randomness;
using Game.UI;
using Game.Systems.Creatures.Surgeries;

namespace Game.Components {
    public sealed partial class BodyModSpotComp : BaseSlotsComp<BodyModSpotComp>,
            IAIGoalProvider, ISpeedUpgradeable, IEnergyEfficiencyUpgradeable,
            ILoadsObj, IAIGoalPriorityListener, IDefPowerProvider {
        private const float DefaultEnergyCost = 25f;
        private const float DefaultChargeCost = 0f;

        // Operating draws EnergyCost, charging a robotic patient draws ChargeCost
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var cost = config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            var charge = config.GetFloat(PropertyIdH.ChargeCost, DefaultChargeCost);
            if (charge > 0f) {
                power.AddConsumption(Mathf.Min(cost, charge), Mathf.Max(cost, charge));
            } else {
                power.AddConsumption(cost);
            }
        }
        public SurgicalOpType SubOperationType {
            get {
                if (opType != SurgicalOpType.ReplaceIrreparableAndMissing) {
                    return opType;
                }
                if (Patient == null) {
                    return SurgicalOpType.RemoveIrreparable;
                }
                bool hasMore;
                int attempts = 0;
                do {
                    hasMore = false;
                    foreach (var slot in Patient.Data.Body.Slots) {
                        if (slot.IsPartRemoved) { continue; }
                        if (!slot.CanRemovePart(false)) { continue; }
                        if (slot.IsUsableWithParent) { continue; }
                        if (slot.HasAttachedParts) {
                            hasMore = true;
                            continue;
                        }
                        return SurgicalOpType.RemoveIrreparable;
                    }
                } while (hasMore && attempts++ < 50);
                return SurgicalOpType.FillMissing;
            }
        }
        /// <param name="goal"></param>
        /// <param name="needsPart">If part is needed, agent will try to pick it up</param>
        /// <param name="needsBodySlot">If body part slot is only one, then this should be true</param>
        /// <returns></returns>
        public static SurgicalOpType GetOpType(AIGoal goal, out bool needsPart,
                out bool needsBodySlot) {
            if (goal.TryGetVar(AIGoalVars.SurgeryType_Int, out int opType)) {
                var ot = (SurgicalOpType)opType;
                ResolveOpType(ot, out needsPart, out needsBodySlot);
                return ot;
            }
            D.Err("OpType not defined for surgery goal! {0}", goal);
            needsPart = false;
            needsBodySlot = false;
            return SurgicalOpType.Undefined;
        }

        private static void ResolveOpType(SurgicalOpType ot,
                out bool needsPart, out bool needsBodySlot) {
            switch (ot) {
                case SurgicalOpType.Install:
                case SurgicalOpType.Swap: {
                    needsPart = true;
                    needsBodySlot = true;
                    break;
                }
                case SurgicalOpType.Remove: {
                    needsPart = false;
                    needsBodySlot = true;
                    break;
                }
                // remaining operations don't need the body part pre-delivered
                default: {
                    // fill missing will be a special case here
                    needsPart = false;
                    needsBodySlot = false;
                    break;
                }
            }
        }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new BodyModSpotComp());
        }
        private ElectricNodeComp elNode;
        private float energyCost;
        private float chargeCost;
        private float chargePerGridTick;
        private string bodyPartSlot;
        private OperatableDeviceComp operatable;
        private SurgicalOpType opType;
        private float opProgress;
        private float operationSpeed;
        private float upgradeSpeed = 1f;
        private float upgradeEnergyEfficiency = 1f;
        private int patientId;
        /// <summary>
        /// Next targeted part for fill missing body parts op type
        /// </summary>
        private uint nextOrderedPartId;
        private Being patient;
        public Being Patient {
            get {
                if (patient == null) {
                    if (patientId == 0) {
                        return null;
                    }
                    patient = S.Beings.Find(patientId);
                    if (patient == null) {
                        D.Err("Could not load patient by id: {0}", patientId);
                        patientId = 0;
                        return null;
                    }
                }
                return patient;
            }
            set {
                patient = value;
                if (value == null) {
                    patientId = 0;
                } else {
                    patientId = value.Id;
                }
            }
        }
        private uint bodyPartDataId;
        public uint ReservedObjId {
            get {
                if (nextOrderedPartId != 0) {
                    return nextOrderedPartId;
                }
                if (bodyPartDataId != 0) {
                    return bodyPartDataId;
                }
                return 0;
            }
        }
        private ObjData bodyPartData;
        private ObjData BodyPartData {
            get {
                if (bodyPartData == null) {
                    if (bodyPartDataId == 0) {
                        return null;
                    }
                    var data = S.Query.FindObjData.Ask(bodyPartDataId);
                    if (data == null) {
                        D.Err("Could not find BodyPartData by id: {0}", bodyPartDataId);
                        bodyPartDataId = 0;
                        return null;
                    }
                    bodyPartData = data;
                }
                return bodyPartData;
            }
            set {
                bodyPartData = value;
                if (value == null) {
                    bodyPartDataId = 0;
                } else {
                    bodyPartDataId = value.Id;
                }
            }
        }
        private int slotDesignation;
        public override int SlotDesignation => slotDesignation;
        private string SlotsTitle => slotDesignation == SlotDesignationH.BodyModSpot_Organic
            ? T.MedicalSlots : T.NounSlots;
        public bool IsSpeedUpgradeable => true;
        public bool IsEnergyEfficiencyUpgradeable => true;
        public int LoadObjSpotIdx => Tile.Transform.InPort;
        public float OperationProgress => opProgress;

        public override void OnLateReady(bool wasLoaded) {
            // no component means the device is fully automatic
            if (!entity.TryGetComponent(out operatable)) {
                D.Err("BodyModSpotComp requires OperatableDeviceComp! {0}",
                    entity.DefinitionId);
            } else {
                operatable.GoalPriority = GoalPriority;
                operatable.OnGoalCancel = OnCancelOperate;
            }
            if (wasLoaded && Tile.IsConstructed) {
                CreateBeingSlots(IconId.CWrench2, SlotsTitle);
            }
            if (wasLoaded && patientId != 0) {
                Ready.When(WhenH.BeingsLoaded, RestorePatient);
            }
        }

        private void OnCancelOperate(AIGoal goal) {
            OnGoalChange(goal, AIGoalState.InProgress, AIGoalState.Cancelled);
        }

        private void RestorePatient() {
            var patient = Patient;
            if (patient == null) {
                Stop(false);
            } else {
                LoadPatient(patient, opType, bodyPartSlot, Tile.Goal, true);
            }
        }

        public override void OnRemove() {
            RemoveSlots();
            Stop(false);
            EjectPatient();
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    elNode = enode;
                    enode.AddAfterTick(this, AfterTickGrid);
                }
            } else if (message == MsgIdH.ConstructionFinished) {
                CreateBeingSlots(IconId.CWrench2, SlotsTitle);
            }
        }
        public bool CanRun() {
            return elNode.IsPowerable && !elNode.HasDeficit && IsUsable;
        }

        // The patient's battery is Offline in here (LoadPatient), so it can't emergency
        // recharge - that rate scales with consumption. The hub has to charge it instead.
        private bool TryChargePatient() {
            return chargePerGridTick > 0f && BeingUtils.TryCharge(Patient.Data, chargePerGridTick);
        }

        // While waiting for a part or an operator, draw only what charging the patient needs
        private void IdleCharge() {
            if (!CanRun()) { return; }
            elNode.SetConsumerWantedInput(TryChargePatient() ? chargeCost : 0f);
        }

        /// <summary>
        /// We'll set this to true if we cancel the goal while continuing surgery
        /// so we won't stop anything in OnGoalChanged
        /// </summary>
        private bool skipStopOnGoalCancel;

        public void Stop(bool isTaskCompleted, bool cancelSurgery = false) {
            Finish(isTaskCompleted, cancelSurgery, null);
        }

        // A failed procedure (failReason != null) is still a finished one: it goes through the
        // same cleanup as a completed procedure, so the patient's remaining queue continues.
        private void Finish(bool isTaskCompleted, bool cancelSurgery, string failReason) {
            if (isTaskCompleted || cancelSurgery) {
                if (Patient != null) {
                    var op = S.Query.GetOngoingSurgicalOp.Ask(Patient.Data);
                    if (op != null) {
                        if (failReason != null) {
                            op.FailReason = failReason;
                            op.State.Set(SurgicalOpState.Failed);
                        } else {
                            op.State.Set(isTaskCompleted ? SurgicalOpState.Completed : SurgicalOpState.Canceled);
                        }
                        S.Query.RegisterSurgicalOperation.Ask(op, false);
                    } else {
                        var hasOps = S.Query.HasPendingSurgeries.Ask(Patient.Data, null);
                        if (hasOps) {
                            D.Err("Ongoing Surgical op doesn't exist at the end of surgery, while there are other surgical ops: {0} -> {1}",
                                opType, Patient);
                        }
                    }
                }
            }
            opType = SurgicalOpType.Undefined;
            bodyPartSlot = null;
            nextOrderedPartId = 0;
            opProgress = 0f;
            EjectBodyPart();
            elNode?.SetConsumerWantedInput(0f);
            bool willContinue = false;
            if (isTaskCompleted) {
                // before ejecting the patient, check if we have any more surgeries for this patient,
                // and if so, let's keep the surgery going
                if (Patient != null) {
                    var nextOp = S.Query.TryNextSurgicalProcedure.Ask(Patient.Data);
                    if (nextOp == null) {
                        EjectPatient();
                    } else {
                        // Only a part delivery needs the operator gone, as the device holds one
                        // goal. A released surgeon was auctioned before the new operator task
                        // could bid, and left for lower priority work between a patient's
                        // procedures (STA-1808)
                        if (OrdersPart(nextOp.OpType) && operatable.TryGetGoal(out var g)) {
                            skipStopOnGoalCancel = true;
                            AIGoal.Cancel(g, G.Ticks);
                            skipStopOnGoalCancel = false;
                        }
                        if (!TryContinueSurgery(nextOp)) {
                            opType = SurgicalOpType.Undefined;
                            EjectPatient();
                        } else {
                            willContinue = true;
                        }
                    }
                }
            } else {
                EjectPatient();
            }
            if (!willContinue) {
                operatable.SetAllowedMindIdsOverride(null);
                HideAllInfo();
                operatable.DismissOperator(isTaskCompleted);
            }
        }

        private void AfterTickGrid() {
            ResolveOpType(opType, out var needsPart, out var needsBodySlot);
            if (opType == SurgicalOpType.Undefined) {
                // sanity check
                if (patientId != 0 || bodyPartDataId != 0)  {
                    if (Patient != null) {
                        D.Err("Operation type undefined but patient present: {0} at {1}", Patient, Tile);
                        EjectPatient();
                    }
                    if (BodyPartData != null) {
                        D.Err("Operation type undefined but body part present: {0} at {1}", BodyPartData, Tile);
                        EjectBodyPart();
                    }
                    operatable.DismissOperator(true);
                }
                return;
            } else if (Patient == null) {
                D.Err("No patient {0} during {1} operation!", Patient, opType);
                Stop(false);
                return;
            }
            if (SurgeryUtils.IsAwakeHostilePatient(Patient.Data)) {
                // Off the table, so the raid he joined can play out. Stop first: clearing
                // the queue first cancels the op's goal, which would run Stop from inside it.
                var data = Patient.Data;
                Stop(false, cancelSurgery: true);
                S.Query.ClearSurgicalOpsFor.Ask(data);
                S.Sys.Log.AddLine(data, T.SurgeryPatientCameTo);
                return;
            }
            if (Patient.Goal != null) {
                D.Err("Patient has a goal on itself! {0}->{1}", Patient.Goal, Patient);
                AIGoal.Cancel(Patient.Goal, G.Ticks, T.NounSurgery);
            }
            var surgery = S.Query.GetOngoingSurgicalOp.Ask(Patient.Data);
            if (surgery == null) {
                D.Warn("Patient has no surgery in progress, aborting operation: {0} | {1}",
                    opType, Patient);
                Stop(false);
                return;
            }

            var subOpType = SubOperationType;

            if (subOpType == SurgicalOpType.FillMissing) {
                // we have to try to order delivery of the next missing body part
                if (CheckNextPartDelivery()) {
                    needsPart = true;
                } else {
                    // there are no parts left?
                    Stop(true);
                    return;
                }
            }

            if (needsPart && BodyPartData == null) {
                // we're waiting for the body part to be loaded
                IdleCharge();
                if (bodyPartDataId == 0 && ReservedObjId == 0 && !HasGoal) {
                    D.Err("Patient is ready for a body part swap, but bodyPartDataId == 0! {0} | {1}",
                        Patient, this);
                    Abort();
                }
                return;
            }

            // burn electricity and install the part
            if (CanRun()) {
                if (operatable.Operator == null && !operatable.IsAutomated) {
                    var goal = operatable.HailOperator(false);
                    if (goal != null) {
                        goal.WithFlag(AIGoalFlags.IgnoreDeadBodies);
                    }
                    IdleCharge();
                } else {
                    var opInput = energyCost / upgradeEnergyEfficiency;
                    // the idle charge draw is consumption too, but it doesn't power an operation
                    if (elNode.IsConsuming && elNode.Node.ConsumerWantedInput >= opInput) {
                        opProgress += operationSpeed * upgradeSpeed
                                * operatable.OperatorEfficiency;
                        TryChargePatient();
                    } else {
                        elNode.SetConsumerWantedInput(opInput);
                    }
                }
            }
            if (opProgress >= 1f) {
                var outcome = ProcedureOutcome.InProgress;
                string failReason = null;
                try {
                    outcome = FinalizeProcedure(subOpType, out failReason);
                } catch (Exception ex) {
                    D.LogEx(ex, "Failed finalizing surgical procedure: {0} -> {1}",
                        opType, subOpType);
                    // retrying would throw again on every tick with the patient stuck inside
                    outcome = ProcedureOutcome.Failed;
                    failReason = T.OperationFailed;
                }
                if (outcome == ProcedureOutcome.Completed) {
                    Stop(true);
                } else if (outcome == ProcedureOutcome.Failed) {
                    Finish(true, false, failReason ?? T.OperationFailed);
                }
                // some procedures will reset the opProgress to 0 and start over
            } else {
                if (Patient.PosIdx != Tile.Transform.InPort) {
                    D.Err("Sanity check, patient not at in port! {0} | {1}",
                        Pos.StringFromTo(Tile.Transform.InPort, Patient.PosIdx), Patient);
                    Stop(false);
                }
                ShowInfoBar(Consts.ColorGreenBar, opProgress,
                    $"{SurgicalOperation.NameFor(opType, true)}: {Units.Percentage(opProgress)}");
            }
        }

        private bool CheckNextPartDelivery() {
            if (nextOrderedPartId != 0) {
                if (BodyPartData == null) {
                    // we are waiting on a part
                    return true;
                } else if (!HasGoal) {
                    // part is delivered, we need to install it
                    operatable.HailOperator(true);
                    return true;
                }
            }

            operatable.DismissOperator(true);
            // 1. for each missing part:
            // 2. see if a parent part is installed
            // 3. try finding available part
            // 4. if part is found, order delivery
            var surgery = S.Query.GetOngoingSurgicalOp.Ask(Patient.Data);
            foreach (var slot in Patient.Data.Body.Slots) {
                // D.Warn("Trying to find installable part for slot: {0}", slot);
                if (slot.IsDestroyed) { continue; }
                if (!slot.IsPartRemoved) { continue; }
                if (slot.Type.IsOptional) { continue; }
                if (slot.BeyondRepairAncestor != null) { continue; }
                var otherOp = S.Query.FindSurgicalOperation.Ask(slot, false);
                if (otherOp?.OpType == SurgicalOpType.Install) {
                    // another operation will handle this
                    continue;
                }
                var part = S.Query.BestInstallableBodyPart.Ask(slot);
                if (part == null) {
                    // D.Warn("Couldn't find part for slot: {0}", slot);
                    // no parts available for this
                    continue;
                }
                D.Warn("Found best installable part {0}", part);

                if (TryOrderPartDelivery(part, surgery)) {
                    if (part.Obj != null) {
                        // not in storage
                        if (part.Obj.StorableObj == null) {
                            D.Err("BodyPart does not have StorableObjComp! {0}", part.Def);
                        } else {
                            part.Obj.StorableObj.IsForbidden = true;
                        }
                    }
                    bodyPartSlot = slot.Id;
                    // D.Warn("Ordered part delivery: {0}", part);
                    if (nextOrderedPartId == part.Id) {
                        D.Err("Trying to order same part more than once! {0}", part);
                        return false;
                    }
                    nextOrderedPartId = part.Id;
                    return true;
                } else {
                    D.Warn("Couldn't order delivery for part: {0}", part);
                    return false;
                }
            }
            D.Warn("Could not find any installable parts for {0}", Patient);
            return false;
        }

        private enum ProcedureOutcome : byte {
            InProgress,
            Completed,
            Failed,
        }

        /// <returns>Completed or Failed if there is nothing else left to do</returns>
        private ProcedureOutcome FinalizeProcedure(SurgicalOpType subOpType, out string failReason) {
            failReason = null;
            ResolveOpType(opType, out var needsPart, out var needsBodySlot);
            switch (subOpType) {
                case SurgicalOpType.FillMissing: {
                    if (BodyPartData == null) {
                        D.Err("No BodyPartData when trying to finalize FillMissing! {0}",
                            D.DumpInstance(this));
                        return ProcedureOutcome.InProgress;
                    }
                    if (bodyPartSlot == null) {
                        D.Err("No bodyPartSlot when trying to finalize FillMissing! {0}",
                            D.DumpInstance(this));
                        return ProcedureOutcome.InProgress;
                    }
                    if (Patient.Data.Body.TryGetSlot(bodyPartSlot, out var sl)) {
                        if (PerformActionOnBodyPartSlot(sl, SurgicalOpType.Install, out _, out _)) {
                            nextOrderedPartId = 0;
                            operatable.DismissOperator(true);
                        } else {
                            D.Err("Failed installing missing body part slot: {0}->{1}",
                                bodyPartSlot, BodyPartData);
                        }
                    } else {
                        D.Err("Failed getting patient body slot: {0} | {1}",
                            bodyPartSlot, Patient);
                    }
                    // if we want to deliver the next part, op is not finished yet
                    if (CheckNextPartDelivery()) {
                        // ordered a new part
                        opProgress = 0f;
                        HideInfoBar();
                        return ProcedureOutcome.InProgress;
                    } else {
                        return ProcedureOutcome.Completed;
                    }
                }
                case SurgicalOpType.RemoveIrreparable: {
                    foreach (var slot in Patient.Data.Body.Slots) {
                        if (slot.IsPartRemoved) { continue; }
                        if (!slot.CanRemovePart(false)) { continue; }
                        // either this has to be destroyed or it should have a destroyed parent
                        if (!slot.State.IsDestroyed && !slot.HasMissingOrDestroyedParent) {
                            continue;
                        }
                        if (slot.HasAttachedParts) { continue; }
                        if (slot.State.IsWelded) { continue; }
                        PerformActionOnBodyPartSlot(slot, SurgicalOpType.Remove, out _, out _);
                        opProgress = 0f;
                        return ProcedureOutcome.InProgress;
                    }
                    // still more work to do
                    if (opType == SurgicalOpType.ReplaceIrreparableAndMissing) {
                        // need to dismiss so we can start bringing missing parts in
                        operatable.DismissOperator(true);
                        opProgress = 0f;
                        return ProcedureOutcome.InProgress;
                    }
                    return ProcedureOutcome.Completed;
                }
                case SurgicalOpType.RemoveAll: {
                    var next = NextSlotToRemoveAll(Patient.Data.Body);
                    if (next != null) {
                        PerformActionOnBodyPartSlot(next, SurgicalOpType.Remove, out _, out _);
                        opProgress = 0f;
                        return ProcedureOutcome.InProgress;
                    }
                    // BeingUtils.RemoveAllBodyParts(Patient, Tile.Transform.WorkSpot);
                    return ProcedureOutcome.Completed;
                }
                default: {
                    if (needsBodySlot) {
                        if (!Patient.Data.Body.TryGetSlot(bodyPartSlot, out var sl)) {
                            D.Err("Failed getting patient body slot: {0} | {1}",
                                bodyPartSlot, Patient);
                            failReason = T.OperationFailed;
                            NotifyProcedureFailed(null, failReason);
                            return ProcedureOutcome.Failed;
                        }
                        PerformActionOnBodyPartSlot(sl, opType, out var failure, out failReason);
                        if (failReason == null) {
                            return ProcedureOutcome.Completed;
                        }
                        // a complication has already been reported by SurgeryUtils, only a
                        // procedure that could not be performed at all still needs a word
                        if (failure == SurgicalOpFailureType.None) {
                            NotifyProcedureFailed(sl, failReason);
                        }
                        return ProcedureOutcome.Failed;
                    } else {
                        D.Err("Default finalize body part mod procedure without body part slot! {0}", opType);
                        return ProcedureOutcome.InProgress;
                    }
                }
            }
        }

        private void NotifyProcedureFailed(BodyPartSlot slot, string reason) {
            var patientData = Patient?.Data;
            if (patientData == null) { return; }
            var surgeon = operatable?.Operator?.Data;
            var performer = surgeon != null ? surgeon.Mind.LinkToShortName : Tile.NameLink;
            var slotName = slot?.NameT ?? T.NounBodyPart;
            var msg = $"{T.SurgicalOpFailed_V3.F(performer, patientData.Mind.LinkToShortName, slotName)}. {reason}";
            if (patientData.IsPlayer || slot?.HoldsMind == true) {
                S.Sig.ShowFanfare.Send(msg);
            }
            S.Sys.Log.AddLine(patientData, msg);
            if (surgeon != null) {
                S.Sys.Log.AddLine(surgeon, msg);
            }
        }

        // Install complications that leave the planned part out of the slot. The others
        // (wound, cripple, weld, fatality) still install it, so the procedure did its job.
        private static string UninstalledReason(SurgicalOpFailureType failure, BodyPartDef part) {
            switch (failure) {
                case SurgicalOpFailureType.DestroyPart:
                case SurgicalOpFailureType.DestroySlot:
                case SurgicalOpFailureType.Rejection:
                    return SurgeryUtils.DescribeFailure(failure, part);
                default:
                    return null;
            }
        }

        private static string NotRemovedReason(SurgicalOpFailureType failure, BodyPartSlot slot,
                BodyPartDef part) {
            if (failure != SurgicalOpFailureType.None && part != null) {
                return SurgeryUtils.DescribeFailure(failure, part);
            }
            return T.SurgicalOpFailedRemove_V1.F(part?.Def.CodexLink ?? slot.NameT);
        }

        // Returns false if the procedure could not be performed at all. failure is the
        // complication that decided the outcome (SurgeryUtils has already reported it), and
        // failReason is set whenever the planned end state was not reached.
        private bool PerformActionOnBodyPartSlot(BodyPartSlot slot,
                SurgicalOpType subObType, out SurgicalOpFailureType failure, out string failReason) {
            failure = SurgicalOpFailureType.None;
            failReason = null;
            if (subObType == SurgicalOpType.Install || subObType == SurgicalOpType.Swap) {
                // an order from before such installs were refused
                var deadEnd = slot.BeyondRepairAncestor;
                if (deadEnd != null) {
                    failReason = T.BodyPartUnusableViaParent_V1.F(deadEnd.NameT);
                    return false;
                }
            }
            bool performed = true;
            var performerLink = Tile.NameLink;
            Obj spawnedPart = null;
            BeingData worker = operatable?.Operator?.Data;
            if (slot.Body.Owner.Species.IsBiological) {
                var slotFatigue = slot.Type.SurgicalFatigue;
                if (slotFatigue <= 0f) {
                    slotFatigue = 0.03f;
                }
                slot.Body.Owner.StatsBeing.SurgicalFatigue.TryIncrease(
                    slotFatigue);
            }
            if (subObType == SurgicalOpType.Install) {
                var installDef = BodyPartData.BodyPart;
                if (slot.InstallPart(S, BodyPartData, worker, true, out var installPartConsumed,
                        out failure, opType, performerLink)) {
                    if (installPartConsumed) {
                        BodyPartData = null;
                    } else {
                        EjectBodyPart();
                    }
                    failReason = UninstalledReason(failure, installDef);
                    return true;
                }
                failReason = T.SurgicalOpFailedInstall_V1.F(installDef.Def.CodexLink);
                return false;
            } else if (subObType == SurgicalOpType.Swap) {
                D.Warn("Swapping part now: {0}", BodyPartData);
                var newDef = BodyPartData.BodyPart;
                var oldDef = slot.InstalledDef;
                if (slot.TryRemovePart(S, out spawnedPart, worker, true, out var extractFailure,
                        true, opType, performerLink)) {
                    D.Warn("Removed part: {0}", spawnedPart);
                    // A swap is one player-facing operation made of two procedures - if the
                    // extraction already failed, the implant must not roll a second failure,
                    // or the patient is punished twice and gets two failure notifications.
                    var canFail = extractFailure == SurgicalOpFailureType.None;
                    if (slot.InstallPart(S, BodyPartData, worker, canFail, out var swapPartConsumed,
                            out failure, opType, performerLink)) {
                        if (swapPartConsumed) {
                            BodyPartData = null;
                        } else {
                            EjectBodyPart();
                        }
                        failReason = UninstalledReason(failure, newDef);
                    } else {
                        // The new part stays loaded and Finish ejects it. The old part is out
                        // already, so it still has to reach the out-port below, and the
                        // ability refresh TryRemovePart skipped for the swap never came.
                        slot.Body.Owner.AfterAbilitiesChanged();
                        failure = extractFailure;
                        failReason = extractFailure != SurgicalOpFailureType.None
                            ? SurgeryUtils.DescribeFailure(extractFailure, oldDef ?? newDef)
                            : T.SurgicalOpFailedInstall_V1.F(newDef.Def.CodexLink);
                        performed = false;
                    }
                } else {
                    D.Err("Failed removing part for swap: {0} / {1}",
                        bodyPartSlot, Patient);
                    failure = extractFailure;
                    failReason = NotRemovedReason(extractFailure, slot, oldDef);
                    EjectOrphanedParts(slot);
                    return false;
                }
            } else if (subObType == SurgicalOpType.Remove) {
                var wasAlive = slot.Body.IsAlive;
                var removeDef = slot.InstalledDef;
                if (!slot.TryRemovePart(S, out spawnedPart, worker, true, out var removeFailure,
                        false, opType, performerLink)) {
                    // a rolled complication (e.g. the part got welded) is a game outcome,
                    // already reported to the player - only an unexplained refusal is an error
                    if (removeFailure == SurgicalOpFailureType.None) {
                        D.Err("Failed to remove body part! {0} / {1}",
                            bodyPartSlot, Patient);
                    } else {
                        D.Warn("Body part not removed ({0}): {1} / {2}",
                            removeFailure, bodyPartSlot, Patient);
                    }
                    failure = removeFailure;
                    failReason = NotRemovedReason(removeFailure, slot, removeDef);
                    EjectOrphanedParts(slot);
                    return false;
                }
                if (wasAlive && !slot.Body.IsAlive) {
                    string msg;
                    if (slot.Body.Owner.Species.IsBiological) {
                        msg = T.DeathByBodyPartRemovalBio_V2;
                    } else {
                        msg = T.DeathByBodyPartRemovalRobo_V2;
                    }
                    msg = msg.F(operatable.Operator?.Data.Mind.LinkToShortName ?? T.Unknown,
                        spawnedPart?.Definition.CodexLink ?? slot.Type.NameT);
                    slot.Body.Owner.Mind.OnDeath(msg, G.Ticks);
                }
            }
            if (spawnedPart != null) {
                EjectRemovedPart(spawnedPart);
            }
            EjectOrphanedParts(slot);
            return performed;
        }

        // Parts the patient needs to live go last and the mind last of all, so a fatal
        // removal does not end the harvest before the other parts are out
        public static BodyPartSlot NextSlotToRemoveAll(Body body) {
            BodyPartSlot vital = null;
            BodyPartSlot mind = null;
            foreach (var slot in body.Slots) {
                if (slot.IsPartRemoved) { continue; }
                if (!slot.CanRemovePart(false)) { continue; }
                if (slot.HasAttachedParts) { continue; }
                if (slot.HoldsMind) {
                    mind ??= slot;
                } else if (slot.IsRequiredForBeingAlive) {
                    vital ??= slot;
                } else {
                    return slot;
                }
            }
            return vital ?? mind;
        }

        private static readonly List<Obj> orphanedParts = new();

        private void EjectOrphanedParts(BodyPartSlot slot) {
            orphanedParts.Clear();
            SurgeryUtils.DetachOrphanedParts(S, slot, orphanedParts);
            for (int i = 0; i < orphanedParts.Count; i++) {
                EjectRemovedPart(orphanedParts[i]);
            }
            orphanedParts.Clear();
        }

        private void EjectRemovedPart(Obj part) {
            part.Transform.TeleportTo(
                EntityUtils.CenterOf(Tile.Transform.OutPort)
                    + Rng.UInsideUnitCircle() * 0.33f);
            if (part.Data.BodyPartState.WoundSeverity >= Severity.Irreparable
                    || part.Data.BodyPartState.Condition < 0.1f) {
                if (part.TryGetComponent<DisposableComp>(out var disp)) {
                    disp.TryOrderDispose(false, out _);
                }
            }
        }

        // Lazy: Register runs before the language loads, so a label made then stays English
        private static StatModifier batteryZeroConsumption;
        private static StatModifier BatteryZeroConsumption => batteryZeroConsumption ??= StatModifier.Sets(0f, T.Offline);

        /// <summary>
        /// Removes the Offline BatteryConsumption modifier applied to robotic patients in
        /// <see cref="LoadPatient"/>. Safe to call even if the modifier was never applied
        /// (e.g. biological patients, or a being that was never actually a patient here) -
        /// also used by BeingsSys to heal patients orphaned outside the normal eject path.
        /// </summary>
        public static void RemoveOfflineConsumption(BeingData data) {
            if (!data.Species.IsBiological) {
                data.StatsBeing.BatteryConsumption.RemoveModifier(BatteryZeroConsumption, false);
            }
        }

        public static bool IsBatteryOffline(BeingData data) {
            return data.StatsBeing.BatteryConsumption.Modifiers?.Contains(BatteryZeroConsumption) == true;
        }

        public bool LoadPatient(Being being, SurgicalOpType opType,
                string bodySlotId, AIGoal goal, bool isLoading) {
            var slot = Slots[0];

            // D.Warn("Loading patient: {0} -> {1} (loading: {2})", G.Ticks, being, isLoading);

            SurgicalOperation surgery = null;
            if (goal != null) {
                // The procedure priority setting is a default: it must not override a
                // priority the player gave this device
                if (GoalPriority == defaultPriority && GoalPriority != goal.GoalPriority
                        && goal.GoalPriority == A.BodyPartProcedurePriority) {
                    SetPriority(goal.GoalPriority);
                }
                if (goal.TryGetVar(AIGoalVars.SurgeryId, out int surgeryId)) {
                    surgery = S.Query.GetSurgicalOp.Ask(surgeryId);
                }
            }
            // when we're loading, surgeries are resolved a bit later
            if (!isLoading && surgery == null) {
                D.Err("Surgery is null after loading patient into the device: {0} | {1} | Goal: {2}",
                    being, Tile, goal);
                surgery = S.Query.GetOngoingSurgicalOp.Ask(being.Data);
                if (surgery == null) {
                    D.Err("PANIC: Surgery is still null for being: {0}", being);
                    return false;
                }
            }

            ResolveOpType(opType, out var needsPart, out var needsBodySlot);

            if (!isLoading && opType != surgery.OpType) {
                D.Err("Surgery op type does not match! {0} | {1}", opType, surgery);
            }

            if (needsBodySlot && !being.Data.Body.HasSlot(bodySlotId)) {
                D.Err("Could not get target body part slot when loading patient: {0} / {1} / {2}",
                    slot, being, goal);
                return false;
            }
            if (slot.ReservedBy != being) {
                if (!slot.Reserve(being)) {
                    D.Err("Failed to reserve the slot for the patient");
                    return false;
                }
            }
            if (slot.Put(being)) {
                if (!being.Data.Species.IsBiological) {
                    being.Data.StatsBeing.BatteryConsumption.TryAddModifier(
                        BatteryZeroConsumption);
                }
                Patient = being;
                this.opType = opType;
                bodyPartSlot = bodySlotId;
                being.Motion.Immobilize(ImmobilizeReasonIdH.Operation, true);
                if (!isLoading) {
                    opProgress = 0f;
                    surgery.State.Set(SurgicalOpState.Ongoing);
                    if (surgery.RestrictedOperatorMindIds != null) {
                        operatable.SetAllowedMindIdsOverride(surgery.RestrictedOperatorMindIds);
                    }
                    AfterLoadPatient(being);
                } else {
                    S.Clock.OnNextUpdate(() => AfterLoadPatient(being));
                }
                return true;
            }
            D.Err("Failed to load patient into slot: {0} / {1}",
                being, goal);
            return false;
        }

        private void AfterLoadPatient(Being being) {
            being.Graphics.SetFacing(SpotRotation);
            being.Graphics.SetVertical(false);
            being.Looks.TogglePosFilters(false, false);
            being.HideAllInfo();
        }

        public bool LoadObj(ObjData partData, AIGoal goal) {
            // D.Warn("Loading body part: {0} / {1}", partData, goal);
            if (partData == null) {
                D.Err("Trying to load null body part data! {0} / {1}",
                    Entity, goal);
                return false;
            }
            BodyPartData = partData;
            partData.State = ObjState.InDevice;
            partData.DeviceId = Tile.Id;
            if (partData.Obj != null) {
                S.Objs.Destroy(partData.Obj);
            }
            ShowInfoIcon(BodyPartData.BodyPart.IconId, BodyPartData.BodyPart.NameT);
            return true;
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Progress", opProgress);
            data.SetInt("PatientId", patientId);
            data.SetInt("Priority", GoalPriority);
            data.SetInt("OpType", (int) opType);
            data.SetUInt("NextPartId", nextOrderedPartId);
            data.SetUInt(AIGoalVars.ObjId_Uint, bodyPartDataId);
            data.SetString(AIGoalVars.BodySlotId_String, bodyPartSlot);
        }

        protected override void OnLoad(ComponentData data) {
            opProgress = data.GetFloat("Progress", 0f);
            patientId = data.GetInt("PatientId", 0);
            GoalPriority = data.GetInt("Priority", GoalPriority);
            nextOrderedPartId = data.GetUInt("NextPartId", 0);
            opType = (SurgicalOpType) data.GetInt("OpType", 0);
            bodyPartDataId = data.GetUInt(AIGoalVars.ObjId_Uint, 0);
            bodyPartSlot = data.GetString(AIGoalVars.BodySlotId_String, null);
        }

        public bool EjectPatient() {
            var p = Patient;
            if (p == null) { return false; }
            // D.Warn("Ejecting patient: {0} -> {1}", G.Ticks, p);
            RemoveOfflineConsumption(p.Data);
            Patient = null;
            Slots[0].Remove(p);
            Slots[0].Unreserve(p);
            p.Motion.Immobilize(ImmobilizeReasonIdH.Operation, false);
            p.Data.Body.CheckCondition(false);
            if (p.IsActive) {
                if (!p.Motion.IsImmobilized && !p.Data.Body.IsIncapacitated) {
                    // D.Warn("adding pos filters for {0}", p);
                    p.Graphics.SetVertical(true);
                    p.Looks.TogglePosFilters(true, false);
                }
                BeingUtils.UpdateBodyInfoIcon(p);
            }
            p.Name.CheckState();
            if (p.Data.Being.IsActive) {
                p.Data.Being.Graphics.RebuildLooks(force: true);
            }
            return true;
        }

        public bool EjectBodyPart() {
            var p = BodyPartData;
            if (p != null) {
                // D.Warn("Ejecting body part: {0}", p);
                p.TryUnreserve(null);
                var ejected = S.Objs.Create(EntityUtils.CenterOf(Tile.Transform.OutPort)
                    + Rng.UInsideUnitCircle() * 0.25f, p);
                BodyPartData = null;
                if (p.BodyPartState.WoundSeverity >= Severity.Irreparable
                        || p.BodyPartState.Condition < 0.1f) {
                    if (ejected.TryGetComponent<DisposableComp>(out var dis)) {
                        dis.TryOrderDispose(false, out _);
                    }
                } else {
                    if (!Arrays.IsEmpty(p.BodyPartState.Wounds)) {
                        for (int i = p.BodyPartState.Wounds.Count - 1; i >= 0; i--) {
                            Wound wound = p.BodyPartState.Wounds[i];
                            wound.Flags &= ~WoundFlags.CantHeal;
                            p.BodyPartState.Wounds[i] = wound;
                        }
                    }
                }
                return true;
            }
            return false;
        }

        public override string ToString() {
            return " * BodyModSpot";
        }
        protected override void OnConfig() {
            // we tick electricity every 10 ticks, thus * 10
            var h = Tunables.BodyPartReplacementDurationHours;
            operationSpeed = (1f / (h * Consts.TicksPerHour)) * 10;
            OnConfigSlots();
            var type = Config.GetString(PropertyIdH.Type);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            chargePerGridTick = Config.GetFloat(PropertyIdH.ChargePerHour, 0f) / Consts.TicksPerHour * 10f;
            chargeCost = Config.GetFloat(PropertyIdH.ChargeCost, DefaultChargeCost);
            GoalPriority = defaultPriority = Config.GetInt(PropertyIdH.DefaultPriority, 4);
            switch (type) {
                case "Organic": {
                    slotDesignation = SlotDesignationH.BodyModSpot_Organic;
                    break;
                }
                case "Robotic": {
                    slotDesignation = SlotDesignationH.BodyModSpot_Robotic;
                    break;
                }
                default: {
                    D.Err("Unknown body part Type in BodyModSlot config: {0} / {1}",
                        type, entity.DefinitionId);
                    break;
                }
            }
        }

        public override bool IsCompatibleWith(BeingData being) {
            return true;
        }

        // The procedures TryContinueSurgery orders a part delivery for
        private static bool OrdersPart(SurgicalOpType type) {
            return type == SurgicalOpType.Install || type == SurgicalOpType.Swap
                || type == SurgicalOpType.FillMissing;
        }

        public bool TryContinueSurgery(SurgicalOperation nextOp) {
            opType = nextOp.OpType;
            switch (opType) {
                case SurgicalOpType.Install:
                case SurgicalOpType.Swap: {
                    if (BodyPartData == null) {
                        if (TryOrderPartDelivery(nextOp.ReservedPart, nextOp)) {
                            ConfirmContinueSurgery(nextOp);
                            return true;
                        }
                        return false;
                    }
                    break;
                }
                case SurgicalOpType.FillMissing: {
                    if (CheckNextPartDelivery()) {
                        ConfirmContinueSurgery(nextOp);
                        return true;
                    } else {
                        // couldn't order the delivery!
                        Stop(false);
                        return false;
                    }
                }
                // any other op type should just run immediately
            }
            ConfirmContinueSurgery(nextOp);
            return true;
        }

        private void ConfirmContinueSurgery(SurgicalOperation nextOp) {
            nextOp.State.Set(SurgicalOpState.Ongoing);
            if (nextOp.TargetSlot != null) {
                bodyPartSlot = nextOp.TargetSlot.Id;
            }
            operatable.HailOperator(true);
        }

        public bool TryStartSurgery(SurgicalOpType opType, ObjData part, AIGoal sourceGoal) {
            // D.Warn("Trying to start surgery: {0} | {1}", opType, G.Ticks);
            if (Patient == null) {
                D.Err("Trying to start surgery while patient is null! {0}", opType);
                return false;
            }
            var surgery = S.Query.GetOngoingSurgicalOp.Ask(Patient.Data);

            // do not fail when we will re-reserve for a different goal
            if (part != null && part.Reservation?.IsFor(sourceGoal) == true) {
                part.TryUnreserve(sourceGoal);
            }

            switch(opType) {
                case SurgicalOpType.Install:
                case SurgicalOpType.Swap: {
                    if (BodyPartData == null) {
                        return TryOrderPartDelivery(part, surgery);
                    }
                    break;
                }
                case SurgicalOpType.FillMissing: {
                    if (CheckNextPartDelivery()) {
                        return true;
                    } else {
                        // couldn't order the delivery!
                        Stop(false, cancelSurgery: true);
                        return false;
                    }
                }
                // any other op type should just run immediately
            }
            // nothing to do, we can wait for operation to finish on its own
            operatable.HailOperator(true);
            return true;
        }

        private bool TryOrderPartDelivery(ObjData part, SurgicalOperation surgery) {
            if (part == null) {
                D.Err("Trying to order null part delivery!");
                Stop(false);
                return false;
            }
            if (HasGoal) {
                D.Err("Already has goal when trying to start surgery! {0}",
                    entity.Goal);
                return false;
            }
            if (!part.IsAvailableIgnoreReservation) {
                D.Err("Target part is not available! {0} | {1}",
                    part, entity.Goal);
                return false;
            }
            var g = CreateGoal(JobTypeIdH.Hauling, IconId.CHaul,
                $"{T.HaulItem}: {T.NounBodyPart}", this)
                    .WithVar(AIGoalVars.ObjId_Uint, part.Id)
                    .ByPlayer(opType == SurgicalOpType.Install || opType == SurgicalOpType.Swap)
                    .WithPriority(GoalPriority)
                    .WithRequiredAbilities(AbilityIdH.Carry, AbilityIdH.DumbLabor)
                    .WithWorldToDesired(AIVarsH.IsObjLoaded, false, true);
            bool reserve = true;
            if (surgery != null) {
                g.WithVar(AIGoalVars.SurgeryId, surgery.Id);
                if (part.Reservation != null) {
                    if (part.Reservation.Surgery == surgery || part.Reservation.IsFor(g)) {
                        reserve = false;
                    }
                }
            }
            if (reserve && !part.TryReserve(g)) {
                D.Err("failed to reserve the body part! {0}", part);
                if (nextOrderedPartId != 0) {
                    nextOrderedPartId = 0;
                } else if (bodyPartDataId != 0) {
                    Stop(false);
                }
                return false;
            }
            if (!entity.TrySetGoal(g)) {
                part.TryUnreserve(g);
                return false;
            }
            return true;
        }

        private void Abort() {
            Stop(false, cancelSurgery: true);
        }

        public string CurrentTaskName {
            get {
                if (opType == SurgicalOpType.Undefined) {
                    return null;
                }
                var part = BodyPartData ?? PendingBodyPart;
                if (part != null) {
                    return $"{SurgicalOperation.NameFor(opType, true)}: {part.BodyPart.NameT}";
                }
                return SurgicalOperation.NameFor(opType, false);
            }
        }
        public int GoalPriority;
        private int defaultPriority;
        public void MovePriority(int direction) {
            SetPriority(UIUtils.AdjustPriority0To5(GoalPriority, direction));
        }
        private void SetPriority(int prio) {
            GoalPriority = prio;
            operatable?.SetPriority(GoalPriority);
        }

        private ObjData PendingBodyPart {
            get {
                uint partId = 0;
                if (bodyPartDataId != 0) {
                    partId = bodyPartDataId;
                } else if (TryGetGoal(out var g)) {
                    if (g.TryGetVar(AIGoalVars.ObjId_Uint, out uint oId)) {
                        partId = oId;
                    }
                }
                if (partId != 0) {
                    return S.Query.FindObjData.Ask(partId);
                }
                return null;
            }
        }

        public void OnGoalChange(AIGoal goal, AIGoalState from, AIGoalState to) {
            if (AIGoal.IsCancelled(to)) {
                if (!skipStopOnGoalCancel) {
                    Stop(false, cancelSurgery: true);
                }
                return;
            }
            if (AIGoal.IsFailed(to) && goal.JobIdH == JobTypeIdH.Hauling) {
                // D.Warn("Mod spot goal failed: {0} | {1}", goal, Tile);
                // check if part is not completely whacked
                var part = PendingBodyPart;
                if (part == null || part.State == ObjState.Destroyed) {
                    if (bodyPartDataId != 0) {
                        D.Warn("Target body part is unavailable! Will try a new part instead: {0}", part);
                        bodyPartDataId = 0;
                        if (!CheckNextPartDelivery()) {
                            D.Warn("Could not find alternative part, ejecting patient: {0}", Patient);
                            Stop(false);
                        }
                    } else {
                        D.Warn("Target body part is unavailable! Ejecting patient: {0} -> {1}", part, Patient);
                        Stop(false);
                    }
                }
            }
        }

        public void OnGoalLoad(AIGoal goal) {
            if (Patient != null) {
                // reposition the being
                Slots[0].Put(Patient);
            }
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
            if (upgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} ->{1}",
                    amt, upgradeSpeed);
                upgradeSpeed = 1f;
            }
        }

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} ->{1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
        }

        public void OnAIGoalPriorityChange(AIGoal goal) {
            GoalPriority = goal.GoalPriority;
            operatable?.SetPriority(GoalPriority);
        }
    }
}
