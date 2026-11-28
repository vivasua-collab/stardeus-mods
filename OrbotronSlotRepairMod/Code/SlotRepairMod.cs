using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.AI;
using Game.Systems.BodyParts;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.OrbotronSlotRepairMod {
    internal static class SlotRepairIds {
        internal const byte RepairDestroyedSlot = 8;
        internal const byte RemoveWeldedPart = 9;
        internal const string ModId = "OrbotronSlotRepairMod";
        internal const string ResearchId = "Research/Robotics/BodySlotRepair";
        internal const string OrbotronSpeciesId = "Orbotron";
        internal const string RemoveWeldedPartKey = "orbotron.slotrepair.removeWeldedPart";

        internal static bool IsOrbotron(BeingData being) {
            return being?.Species != null
                && string.Equals(being.Species.Id, OrbotronSpeciesId, StringComparison.Ordinal);
        }

        internal static bool IsWeldedRemovablePart(BodyPartSlot slot) {
            return slot != null && !slot.IsPartRemoved
                && slot.InstalledDef != null && slot.State != null
                && slot.State.IsWelded && slot.Type.IsRemovable;
        }
    }

    public static class SlotRepairModInit {
        [RuntimeInitializeOnLoadMethod]
        private static void Init() {
            if (Harmony.HasAnyPatches(SlotRepairIds.ModId)) { return; }
            new Harmony(SlotRepairIds.ModId).PatchAll(typeof(SlotRepairModInit).Assembly);
            D.Warn("[OrbotronSlotRepairMod] Harmony patches registered.");
        }
    }

    // Build the patient-side transport goal directly, following the vanilla install path.
    // TryOrderRemove is not suitable for these custom procedures: it rejects empty slots
    // (IsPartRemoved) and also refuses a living, non-player patient unless incapacitated.
    internal static class CustomSurgeryGoalFactory {
        internal static bool TryOrder(BodyComp body, SurgicalOperation op, string iconId) {
            var patient = body?.Being;
            if (patient == null || !patient.IsActive || patient.Data == null) {
                op.FailReason = T.NotAvailable;
                return false;
            }
            if (op.TargetBeing != patient.Data || op.TargetSlot == null
                    || op.TargetSlot.Body.Owner != patient.Data) {
                op.FailReason = "Surgical operation target does not match its patient.";
                op.State.Set(SurgicalOpState.Failed);
                return false;
            }
            if (body.HasExistingGoal(false, out var existingReason)) {
                op.FailReason = existingReason;
                return false;
            }
            if (!op.TargetSlot.Body.HasSurgeryFacilities(out var deviceDefIdH)) {
                op.FailReason = $"{T.RequiredDeviceIsNotAvailable}: {Texts.LinkToCodex(deviceDefIdH)}";
                return false;
            }

            var goal = AIGoal.Create(JobTypeIdH.Task, T.PartPerformReplacement, body, G.Ticks)
                .WithIcon(iconId)
                .WithVar(AIGoalVars.BodySlotId_String, op.TargetSlot.Id)
                .WithVar(AIGoalVars.SurgeryType_Int, (int)op.OpType)
                .WithVar(AIGoalVars.SurgeryId, op.Id)
                .WithWorldToDesired(AIVarsH.IsPatientDelivered, false, true)
                .WithOverwritePriority(1)
                .WithPriority(A.BodyPartProcedurePriority)
                .WithRequiredAbilities(AbilityIdH.Move)
                .ByPlayer(true)
                .WriteInitialState();

            // Keep the same hub-availability gate used by vanilla BodyComp surgery goals.
            var checkMethod = AccessTools.Method(typeof(BodyComp), "CanPerformSurgeryNow");
            if (checkMethod == null) {
                op.FailReason = "Could not resolve the vanilla Assembly Hub availability check.";
                return false;
            }
            try {
                goal.CheckDoable = (AIGoal.AIGoalCheck)Delegate.CreateDelegate(
                    typeof(AIGoal.AIGoalCheck), body, checkMethod);
            } catch (Exception ex) {
                D.LogEx(ex, "Failed binding the vanilla surgery availability check.");
                op.FailReason = T.Error;
                return false;
            }

            AIAgentComp forcedAgent = null;
            if (!patient.Motion.IsImmobilized && goal.CanBeDoneBy(patient.AIAgent)) {
                forcedAgent = patient.AIAgent;
            }
            if (body.Entity.TrySetGoal(goal, forcedAgent)) {
                op.AssociatedGoal = goal;
                op.FailReason = null;
                D.Warn("[CustomSurgery] Created patient transport goal for {0}: {1}",
                    patient, op);
                return true;
            }

            op.FailReason = T.AdRejUndefined;
            D.Warn("[CustomSurgery] Could not assign patient transport goal for {0}: {1}",
                patient, op);
            return false;
        }
    }

    // Route repairs through a dedicated patient transport goal. The destroyed socket is
    // already empty, so the vanilla Remove operation is both semantically wrong and rejected.
    [HarmonyPatch(typeof(BodyComp), nameof(BodyComp.TryOrderOp))]
    internal static class SlotRepairOrderPatch {
        private static bool Prefix(BodyComp __instance, SurgicalOperation op,
                ref bool __result) {
            var opType = (byte)op.OpType;
            if (opType != SlotRepairIds.RepairDestroyedSlot
                    && opType != SlotRepairIds.RemoveWeldedPart) { return true; }

            var slot = op.TargetSlot;
            if (opType == SlotRepairIds.RepairDestroyedSlot) {
                if (slot == null || !SlotRepairIds.IsOrbotron(slot.Body.Owner)
                        || !SlotRepairIds.IsOrbotron(op.TargetBeing)
                        || !slot.IsDestroyed || !slot.IsPartRemoved
                        || slot.InstalledDef != null) {
                    op.FailReason = "Target is not an empty destroyed Orbotron body slot.";
                    op.State.Set(SurgicalOpState.Failed);
                    __result = false;
                    return false;
                }
            } else {
                if (slot == null || !SlotRepairIds.IsOrbotron(slot.Body.Owner)
                        || !SlotRepairIds.IsOrbotron(op.TargetBeing)
                        || !SlotRepairIds.IsWeldedRemovablePart(slot)) {
                    op.FailReason = "Target is not a welded removable Orbotron body part.";
                    op.State.Set(SurgicalOpState.Failed);
                    __result = false;
                    return false;
                }
            }

            __result = CustomSurgeryGoalFactory.TryOrder(__instance, op,
                opType == SlotRepairIds.RepairDestroyedSlot
                    ? IconId.CBodyReplaceBad : IconId.CBodyRemoveBad);
            return false;
        }
    }

    // Custom single-slot operations must carry the slot ID through the vanilla
    // Surgery_GoTo / CarryPatient actions and into BodyModSpotComp.LoadPatient.
    [HarmonyPatch(typeof(BodyModSpotComp), "ResolveOpType")]
    internal static class SlotRepairResolveOpTypePatch {
        private static bool Prefix(SurgicalOpType ot, ref bool needsPart,
                ref bool needsBodySlot) {
            if ((byte)ot != SlotRepairIds.RepairDestroyedSlot
                    && (byte)ot != SlotRepairIds.RemoveWeldedPart) { return true; }
            needsPart = false;
            needsBodySlot = true;
            return false;
        }
    }

    // Start the queue head immediately after the player orders it. The stock global
    // queue ticks only every ten minutes and waits while the patient has another goal.
    internal static class SlotRepairQueueKick {
        internal static void TryStartNext(Being being) {
            if (being == null || !being.IsActive) { return; }

            var currentGoal = being.Goal;
            if (currentGoal != null) {
                var operations = A.S.Query.GetAllSurgeries.Ask();
                bool isCurrentSurgery = operations.Any(x => x.TargetBeing == being.Data
                    && (x.State.Value == SurgicalOpState.Preparing
                        || x.State.Value == SurgicalOpState.Ongoing)
                    && x.AssociatedGoal == currentGoal
                    && AIGoal.IsActive(x.AssociatedGoal));
                if (!isCurrentSurgery) {
                    // AIGoal.Interrupt dereferences Agent for non-interruptable goals.
                    // Do not call it when there is no agent or when the agent is not disabled.
                    if (currentGoal.IsNonInterruptable
                            && (currentGoal.Agent == null || !currentGoal.Agent.IsDisabled)) {
                        return;
                    }
                    if (!AIGoal.Interrupt(currentGoal, G.Ticks, T.NounSurgery)) {
                        return;
                    }
                    // Interrupt clears the assigned worker, but does not unset Entity.Goal.
                    // ClearGoal performs the target-side cleanup required by BodyComp.
                    being.ClearGoal(T.NounSurgery);
                }
            }

            if (being.Goal != null || !being.TryGetComponent<BodyComp>(out var body)) {
                return;
            }
            var nextOp = A.S.Query.TryNextSurgicalProcedure.Ask(being.Data);
            if (nextOp != null && body.TryOrderOp(nextOp)) {
                nextOp.State.Set(SurgicalOpState.Preparing);
            }
        }
    }

    // Body-part operations are selected in SurgeryChoiceView, not in the body diagram
    // builder (SurgeriesHandler.SpawnBodyPartWidgets). A postfix also runs when the
    // vanilla popup returns early, both for a destroyed slot and for a welded part:
    // the welded branch shows the warning and returns before any footer button exists.
    [HarmonyPatch(typeof(SurgeryChoiceView), "LoadPopupContent")]
    internal static class SlotRepairChoicePatch {
        private static void Postfix(SurgeryChoiceView __instance, Lifetime lifetime,
                WGroup wFooter) {
            var slot = AccessTools.Field(typeof(SurgeryChoiceView), "slot")
                ?.GetValue(__instance) as BodyPartSlot;
            if (slot == null || !SlotRepairIds.IsOrbotron(slot.Body.Owner)
                    || !IsResearchUnlocked()) { return; }

            if (slot.IsDestroyed && slot.IsPartRemoved && slot.InstalledDef == null) {
                AddFooterButton(__instance, lifetime, wFooter, "Repair Damaged Slot",
                    IconId.CWrench2, SlotRepairIds.RepairDestroyedSlot, slot);
                return;
            }
            if (SlotRepairIds.IsWeldedRemovablePart(slot)) {
                AddFooterButton(__instance, lifetime, wFooter,
                    SlotRepairIds.RemoveWeldedPartKey.T(), IconId.CBodyRemoveBad,
                    SlotRepairIds.RemoveWeldedPart, slot);
            }
        }

        private static void AddFooterButton(SurgeryChoiceView view, Lifetime lifetime,
                WGroup wFooter, string label, string icon, byte opType, BodyPartSlot slot) {
            var index = wFooter.Container.childCount;
            var button = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WFooterItem_DeleteButton, wFooter.Container, index);
            button.Load(label, () => {
                var op = new SurgicalOperation {
                    TargetBeing = slot.Body.Owner,
                    TargetSlot = slot,
                    OpType = (SurgicalOpType)opType,
                    Icon = icon,
                    Desc = label
                };
                if (!A.S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                    A.S.Sig.ShowErrorMessage.Send(op.FailReason
                        ?? $"Could not queue the {label} operation.");
                } else {
                    SlotRepairQueueKick.TryStartNext(slot.Body.Owner.Being);
                    A.S.UIQuery.ShowContextPopup.Ask(view, false);
                }
            });
            wFooter.SetActiveFast(true);
        }

        private static bool IsResearchUnlocked() {
            var progress = A.S.Research.Get(Hashes.S(SlotRepairIds.ResearchId));
            return progress != null && progress.IsUnlocked;
        }
    }

    [HarmonyPatch(typeof(SurgicalOperation), nameof(SurgicalOperation.NameFor))]
    internal static class SlotRepairOperationNamePatch {
        private static void Postfix(SurgicalOpType type, ref string __result) {
            if ((byte)type == SlotRepairIds.RepairDestroyedSlot) {
                __result = "Repair Damaged Slot";
            }
        }

        // A prefix, so the vanilla switch does not log "Unhandled name" for the
        // custom type while the repaired-slot postfix above stays untouched.
        private static bool Prefix(SurgicalOpType type, ref string __result) {
            if ((byte)type != SlotRepairIds.RemoveWeldedPart) { return true; }
            __result = SlotRepairIds.RemoveWeldedPartKey.T();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class SlotRepairFinalizePatch {
        private static MethodInfo TargetMethod() {
            return AccessTools.Method(typeof(BodyModSpotComp), "FinalizeProcedure");
        }

        private static bool Prefix(object __instance, SurgicalOpType subOpType,
                ref string failReason, ref object __result) {
            if ((byte)subOpType == SlotRepairIds.RemoveWeldedPart) {
                return RemoveWeldedProcedure(__instance, ref failReason, ref __result);
            }
            if ((byte)subOpType != SlotRepairIds.RepairDestroyedSlot) { return true; }

            var patient = AccessTools.Property(typeof(BodyModSpotComp), "Patient")
                .GetValue(__instance, null) as Being;
            var slotId = AccessTools.Field(typeof(BodyModSpotComp), "bodyPartSlot")
                .GetValue(__instance) as string;
            if (patient == null || !SlotRepairIds.IsOrbotron(patient.Data)
                    || string.IsNullOrEmpty(slotId)) {
                failReason = "Repair operation requires an Orbotron patient and a target slot.";
                SetOutcome(ref __result, 2);
                return false;
            }

            var slot = patient.Data.Body.Slots.FirstOrDefault(x => x.Id == slotId);
            if (slot == null || !slot.IsDestroyed) {
                failReason = "Target slot is no longer damaged.";
                SetOutcome(ref __result, 2);
                return false;
            }

            // A destroyed slot is already empty (IsPartRemoved=true, state=null).
            // Clearing this flag makes the ordinary installation path available again.
            slot.IsDestroyed = false;
            AccessTools.Method(slot.Body.GetType(), "CheckCondition")
                ?.Invoke(slot.Body, new object[] { false });

            failReason = null;
            SetOutcome(ref __result, 1);
            return false;
        }

        private static void SetOutcome(ref object result, int value) {
            var method = TargetMethod();
            result = Enum.ToObject(method.ReturnType, value);
        }

        // Extract a welded part through the vanilla removal routine. The welded flag
        // blocks CanRemovePart, so it is lifted only for the duration of the call and
        // restored if the removal fails: an installed part must never silently lose
        // its welded status. canFail:false keeps the surgical failure roll out, so the
        // extraction itself cannot weld the part again.
        private static bool RemoveWeldedProcedure(object __instance,
                ref string failReason, ref object __result) {
            var patient = AccessTools.Property(typeof(BodyModSpotComp), "Patient")
                .GetValue(__instance, null) as Being;
            var slotId = AccessTools.Field(typeof(BodyModSpotComp), "bodyPartSlot")
                .GetValue(__instance) as string;
            if (patient == null || !SlotRepairIds.IsOrbotron(patient.Data)
                    || string.IsNullOrEmpty(slotId)) {
                failReason = "Welded part removal requires an Orbotron patient and a target slot.";
                SetOutcome(ref __result, 2);
                return false;
            }

            if (!patient.Data.Body.TryGetSlot(slotId, out var slot)
                    || !SlotRepairIds.IsWeldedRemovablePart(slot)) {
                failReason = "Target body part is no longer a welded removable part.";
                SetOutcome(ref __result, 2);
                return false;
            }

            var weldedFlags = slot.State.Flags;
            slot.State.Flags = weldedFlags & ~BodyPartStateFlags.Welded;

            var operatable = AccessTools.Field(typeof(BodyModSpotComp), "operatable")
                ?.GetValue(__instance) as OperatableDeviceComp;
            var worker = operatable?.Operator?.Data;
            var wasAlive = slot.Body.IsAlive;
            var s = AccessTools.Field(__instance.GetType(), "S")
                ?.GetValue(__instance) as GameState;
            var removed = slot.TryRemovePart(s, out var spawnedPart, worker,
                false, out _, false, (SurgicalOpType)SlotRepairIds.RemoveWeldedPart);

            if (!removed) {
                slot.State.Flags = weldedFlags;
                failReason = T.OperationFailed;
                SetOutcome(ref __result, 2);
                return false;
            }

            // Mirror the vanilla Remove finalize path (BodyModSpotComp): death notice
            // when the removal was fatal, then the out-port eject and orphan detach.
            if (wasAlive && !slot.Body.IsAlive) {
                var performer = worker?.Mind.LinkToShortName ?? T.Unknown;
                var msg = T.DeathByBodyPartRemovalRobo_V2.F(performer,
                    spawnedPart?.Definition.CodexLink ?? slot.Type.NameT);
                slot.Body.Owner.Mind.OnDeath(msg, G.Ticks);
            }
            if (spawnedPart != null) {
                AccessTools.Method(typeof(BodyModSpotComp), "EjectRemovedPart")
                    ?.Invoke(__instance, new object[] { spawnedPart });
            }
            AccessTools.Method(typeof(BodyModSpotComp), "EjectOrphanedParts")
                ?.Invoke(__instance, new object[] { slot });

            failReason = null;
            SetOutcome(ref __result, 1);
            return false;
        }
    }
}
