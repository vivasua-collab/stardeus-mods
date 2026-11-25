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
        internal const string ModId = "OrbotronSlotRepairMod";
    }

    public static class SlotRepairModInit {
        [RuntimeInitializeOnLoadMethod]
        private static void Init() {
            if (Harmony.HasAnyPatches(SlotRepairIds.ModId)) { return; }
            new Harmony(SlotRepairIds.ModId).PatchAll(typeof(SlotRepairModInit).Assembly);
            D.Warn("[OrbotronSlotRepairMod] Harmony patches registered.");
        }
    }

    // The vanilla queue creates patient goals through BodyComp.TryOrderOp, but that
    // method only knows Remove / Install / Swap. Reuse its remove-task setup, then
    // restore our custom operation type in the AI goal so the Assembly Hub sees it.
    [HarmonyPatch(typeof(BodyComp), nameof(BodyComp.TryOrderOp))]
    internal static class SlotRepairOrderPatch {
        private static bool Prefix(BodyComp __instance, SurgicalOperation op,
                ref bool __result) {
            if ((byte)op.OpType != SlotRepairIds.RepairDestroyedSlot) { return true; }

            var slot = op.TargetSlot;
            if (slot == null || slot.IsPartRemoved || !slot.IsDestroyed) {
                op.FailReason = "Target slot is no longer damaged.";
                op.State.Set(SurgicalOpState.Failed);
                __result = false;
                return false;
            }

            var method = AccessTools.Method(typeof(BodyComp), "TryOrderRemove");
            if (method == null) {
                op.FailReason = "Could not resolve the vanilla body-part operation task.";
                op.State.Set(SurgicalOpState.Failed);
                __result = false;
                return false;
            }

            __result = (bool)method.Invoke(__instance, new object[] { slot, op });
            if (__result && op.AssociatedGoal != null) {
                op.AssociatedGoal.WithVar(AIGoalVars.SurgeryType_Int, (int)op.OpType);
            }
            return false;
        }
    }

    // Custom single-slot operations must carry the slot ID through the vanilla
    // Surgery_GoTo / CarryPatient actions and into BodyModSpotComp.LoadPatient.
    [HarmonyPatch(typeof(BodyModSpotComp), "ResolveOpType")]
    internal static class SlotRepairResolveOpTypePatch {
        private static bool Prefix(SurgicalOpType ot, ref bool needsPart,
                ref bool needsBodySlot) {
            if ((byte)ot != SlotRepairIds.RepairDestroyedSlot) { return true; }
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
                if (!isCurrentSurgery
                        && !AIGoal.Interrupt(currentGoal, G.Ticks, T.NounSurgery)) {
                    return;
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
    // vanilla popup returns early for a destroyed slot.
    [HarmonyPatch(typeof(SurgeryChoiceView), "LoadPopupContent")]
    internal static class SlotRepairChoicePatch {
        private static void Postfix(SurgeryChoiceView __instance, Lifetime lifetime,
                WGroup wFooter) {
            var slot = AccessTools.Field(typeof(SurgeryChoiceView), "slot")
                ?.GetValue(__instance) as BodyPartSlot;
            if (slot == null || !slot.IsDestroyed || slot.Body?.Owner?.Species == null
                    || slot.Body.Owner.Species.IsBiological) { return; }

            var index = wFooter.Container.childCount;
            var button = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WFooterItem_DeleteButton, wFooter.Container, index);
            button.Load("Repair Damaged Slot", () => {
                var op = new SurgicalOperation {
                    TargetBeing = slot.Body.Owner,
                    TargetSlot = slot,
                    OpType = (SurgicalOpType)SlotRepairIds.RepairDestroyedSlot,
                    Icon = IconId.CWrench2,
                    Desc = "Repair Damaged Slot"
                };
                if (!A.S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                    A.S.Sig.ShowErrorMessage.Send(op.FailReason ?? "Could not queue slot repair.");
                } else {
                    SlotRepairQueueKick.TryStartNext(slot.Body.Owner);
                    A.S.UIQuery.ShowContextPopup.Ask(__instance, false);
                }
            });
            wFooter.SetActiveFast(true);
        }
    }

    [HarmonyPatch(typeof(SurgicalOperation), nameof(SurgicalOperation.NameFor))]
    internal static class SlotRepairOperationNamePatch {
        private static void Postfix(SurgicalOpType type, ref string __result) {
            if ((byte)type == SlotRepairIds.RepairDestroyedSlot) {
                __result = "Repair Damaged Slot";
            }
        }
    }

    [HarmonyPatch]
    internal static class SlotRepairFinalizePatch {
        private static MethodInfo TargetMethod() {
            return AccessTools.Method(typeof(BodyModSpotComp), "FinalizeProcedure");
        }

        private static bool Prefix(object __instance, SurgicalOpType subOpType,
                ref string failReason, ref object __result) {
            if ((byte)subOpType != SlotRepairIds.RepairDestroyedSlot) { return true; }

            var patient = AccessTools.Property(typeof(BodyModSpotComp), "Patient")
                .GetValue(__instance, null) as Being;
            var slotId = AccessTools.Field(typeof(BodyModSpotComp), "bodyPartSlot")
                .GetValue(__instance) as string;
            if (patient == null || string.IsNullOrEmpty(slotId)) {
                failReason = "Repair operation has no patient or target slot.";
                SetOutcome(ref __result, 2);
                return false;
            }

            var slot = patient.Data.Body.Slots.FirstOrDefault(x => x.Id == slotId);
            if (slot == null || !slot.IsDestroyed) {
                failReason = "Target slot is no longer damaged.";
                SetOutcome(ref __result, 2);
                return false;
            }

            slot.IsDestroyed = false;
            slot.UpdateCondition(false, false, forceUpdateHierarchy: true);
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
    }
}
