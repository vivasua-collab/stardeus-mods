using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
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
