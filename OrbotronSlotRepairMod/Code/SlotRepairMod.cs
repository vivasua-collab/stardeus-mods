using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Game.CodeGen;
using Game.Components;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Creatures.Surgeries;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.OrbotronSlotRepairMod {
    internal static class SlotRepairIds {
        // Values are intentionally outside the vanilla enum range. Keep unique across
        // independently installed test mods.
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

    [HarmonyPatch(typeof(SurgeriesHandler), "SpawnBodyPartWidgets")]
    internal static class SlotRepairSelectionPatch {
        private static object ReadMember(object obj, string name) {
            if (obj == null) { return null; }
            var t = obj.GetType();
            return AccessTools.Field(t, name)?.GetValue(obj)
                ?? AccessTools.Property(t, name)?.GetValue(obj, null);
        }

        private static void Postfix(object __instance, Lifetime life, BeingData b) {
            if (b == null || b.Species == null || b.Species.IsBiological) { return; }
            var widget = ReadMember(__instance, "widget");
            var bodyContainer = ReadMember(widget, "BodyContainer");
            var container = ReadMember(bodyContainer, "Container");
            if (container == null) { return; }

            foreach (var slot in b.Body.Slots) {
                if (!slot.IsDestroyed) { continue; }
                var button = UISpawn.Widget<WButtonText>(life,
                    UIPrefabId.WContent2ButtonSimple, (Transform)container, -1);
                button.Load("Repair Damaged Slot: " + slot.NameT, () => {
                    var op = new SurgicalOperation {
                        TargetBeing = b,
                        TargetSlot = slot,
                        OpType = (SurgicalOpType)SlotRepairIds.RepairDestroyedSlot,
                        Icon = IconId.CWrench2,
                        Desc = "Repair Damaged Slot"
                    };
                    if (!A.S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                        A.S.Sig.ShowErrorMessage.Send(op.FailReason ?? "Could not queue slot repair.");
                    }
                });
            }
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
        private static MethodBase TargetMethod() {
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
            SetOutcome(ref __result, 1); // ProcedureOutcome.Completed
            return false;
        }

        private static void SetOutcome(ref object result, int value) {
            var method = TargetMethod();
            result = Enum.ToObject(method.ReturnType, value);
        }
    }
}
