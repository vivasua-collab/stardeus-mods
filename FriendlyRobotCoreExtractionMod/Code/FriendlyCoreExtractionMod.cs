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

namespace Game.FriendlyRobotCoreExtractionMod {
    internal static class CoreExtractionIds {
        internal const byte SafeCoreExtraction = 9;
        internal const string ModId = "FriendlyRobotCoreExtractionMod";
        internal const string ResearchId = "Research/Robotics/SafeCoreExtraction";
    }

    public static class FriendlyCoreExtractionModInit {
        [RuntimeInitializeOnLoadMethod]
        private static void Init() {
            if (Harmony.HasAnyPatches(CoreExtractionIds.ModId)) { return; }
            new Harmony(CoreExtractionIds.ModId).PatchAll(typeof(FriendlyCoreExtractionModInit).Assembly);
            D.Warn("[FriendlyRobotCoreExtractionMod] Harmony patches registered.");
        }
    }

    [HarmonyPatch(typeof(SurgeriesHandler), "SpawnBodyPartWidgets")]
    internal static class SafeCoreSelectionPatch {
        private static object ReadMember(object obj, string name) {
            if (obj == null) { return null; }
            var t = obj.GetType();
            return AccessTools.Field(t, name)?.GetValue(obj)
                ?? AccessTools.Property(t, name)?.GetValue(obj, null);
        }

        private static bool IsResearchUnlocked() {
            var progress = A.S.Research.Get(Hashes.S(CoreExtractionIds.ResearchId));
            return progress != null && progress.IsUnlocked;
        }

        private static void Postfix(object __instance, Lifetime life, BeingData b) {
            if (b == null || !b.IsFriendly || b.Species == null || b.Species.IsBiological) { return; }
            if (!IsResearchUnlocked()) { return; }

            var widget = ReadMember(__instance, "widget");
            var bodyContainer = ReadMember(widget, "BodyContainer");
            var container = ReadMember(bodyContainer, "Container");
            if (container == null) { return; }

            foreach (var slot in b.Body.Slots) {
                if (!slot.HoldsMind || slot.Type.Id != "Core" || slot.IsPartRemoved
                        || slot.InstalledDef == null) { continue; }
                var button = UISpawn.Widget<WButtonText>(life,
                    UIPrefabId.WContent2ButtonSimple, (Transform)container, -1);
                button.Load("Safe Core Extraction: " + slot.NameT, () => {
                    var op = new SurgicalOperation {
                        TargetBeing = b,
                        TargetSlot = slot,
                        OpType = (SurgicalOpType)CoreExtractionIds.SafeCoreExtraction,
                        Icon = IconId.CBodyRemoveOne,
                        Desc = "Safe Core Extraction"
                    };
                    if (!A.S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                        A.S.Sig.ShowErrorMessage.Send(op.FailReason ?? "Could not queue safe Core extraction.");
                    }
                });
            }
        }
    }

    [HarmonyPatch(typeof(SurgicalOperation), nameof(SurgicalOperation.NameFor))]
    internal static class SafeCoreOperationNamePatch {
        private static void Postfix(SurgicalOpType type, ref string __result) {
            if ((byte)type == CoreExtractionIds.SafeCoreExtraction) {
                __result = "Safe Core Extraction";
            }
        }
    }

    [HarmonyPatch]
    internal static class SafeCoreFinalizePatch {
        private static MethodBase TargetMethod() {
            return AccessTools.Method(typeof(BodyModSpotComp), "FinalizeProcedure");
        }

        private static bool Prefix(object __instance, SurgicalOpType subOpType,
                ref string failReason, ref object __result) {
            if ((byte)subOpType != CoreExtractionIds.SafeCoreExtraction) { return true; }

            var patient = AccessTools.Property(typeof(BodyModSpotComp), "Patient")
                .GetValue(__instance, null) as Being;
            var slotId = AccessTools.Field(typeof(BodyModSpotComp), "bodyPartSlot")
                .GetValue(__instance) as string;
            if (patient == null || string.IsNullOrEmpty(slotId)) {
                failReason = "Safe Core extraction has no patient or target slot.";
                SetOutcome(ref __result, 2);
                return false;
            }

            var slot = patient.Data.Body.Slots.FirstOrDefault(x => x.Id == slotId);
            if (slot == null || !slot.HoldsMind || slot.Type.Id != "Core"
                    || slot.IsPartRemoved || slot.InstalledDef == null
                    || !patient.Data.IsFriendly || patient.Data.Species.IsBiological) {
                failReason = "Target is not a valid friendly robotic Core.";
                SetOutcome(ref __result, 2);
                return false;
            }

            var method = AccessTools.Method(typeof(BodyModSpotComp),
                "PerformActionOnBodyPartSlot");
            var args = new object[] { slot, SurgicalOpType.Remove, null, null };
            var success = (bool)method.Invoke(__instance, args);
            failReason = args[3] as string;
            SetOutcome(ref __result, success ? 1 : 2);
            return false;
        }

        private static void SetOutcome(ref object result, int value) {
            var method = TargetMethod();
            result = Enum.ToObject(method.ReturnType, value);
        }
    }

    [HarmonyPatch]
    internal static class SafeCoreNoFailureRollPatch {
        private static MethodBase TargetMethod() {
            return AccessTools.GetDeclaredMethods(typeof(BodyPartSlot))
                .First(m => m.Name == "TryRemovePart" && m.GetParameters().Length == 8
                    && m.GetParameters()[7].ParameterType == typeof(string));
        }

        private static void Prefix(BodyPartSlot __instance, object[] __args) {
            if (__args == null || __args.Length != 8) { return; }
            if ((byte)__args[6] != CoreExtractionIds.SafeCoreExtraction) { return; }
            if (!__instance.HoldsMind || __instance.Type.Id != "Core") { return; }
            if (__instance.Body.Owner.Species.IsBiological || !__instance.Body.Owner.IsFriendly) { return; }
            __args[3] = false; // canFail = false, only for this custom operation
        }
    }
}
