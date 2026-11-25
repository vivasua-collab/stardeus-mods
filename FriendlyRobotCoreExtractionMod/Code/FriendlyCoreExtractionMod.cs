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

    // The vanilla queue creates patient goals through BodyComp.TryOrderOp, but that
    // method only knows Remove / Install / Swap. Reuse its remove-task setup, then
    // restore our custom operation type in the AI goal so the Assembly Hub sees it.
    [HarmonyPatch(typeof(BodyComp), nameof(BodyComp.TryOrderOp))]
    internal static class SafeCoreOrderPatch {
        private static bool Prefix(BodyComp __instance, SurgicalOperation op,
                ref bool __result) {
            if ((byte)op.OpType != CoreExtractionIds.SafeCoreExtraction) { return true; }

            var slot = op.TargetSlot;
            if (slot == null || !slot.HoldsMind || slot.Type.Id != "Core"
                    || slot.IsPartRemoved || slot.IsDestroyed || slot.InstalledDef == null
                    || !slot.CanRemovePart(false)
                    || !op.TargetBeing.IsFriendly || op.TargetBeing.Species.IsBiological) {
                op.FailReason = "Target is not a valid removable friendly robotic Core.";
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

    // Make the custom operation behave as a single-slot operation in the vanilla
    // AI action chain, so the selected Core slot is carried into the Assembly Hub.
    [HarmonyPatch(typeof(BodyModSpotComp), "ResolveOpType")]
    internal static class SafeCoreResolveOpTypePatch {
        private static bool Prefix(SurgicalOpType ot, ref bool needsPart,
                ref bool needsBodySlot) {
            if ((byte)ot != CoreExtractionIds.SafeCoreExtraction) { return true; }
            needsPart = false;
            needsBodySlot = true;
            return false;
        }
    }

    // Start the queue head immediately after the player orders it. The stock global
    // queue ticks only every ten minutes and waits while the patient has another goal.
    internal static class SafeCoreQueueKick {
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

    // The actual per-slot operation menu is SurgeryChoiceView. The old patch targeted
    // the body diagram construction and created unrelated buttons outside the popup.
    [HarmonyPatch(typeof(SurgeryChoiceView), "LoadPopupContent")]
    internal static class SafeCoreChoicePatch {
        private static void Postfix(SurgeryChoiceView __instance, Lifetime lifetime,
                WGroup wFooter) {
            var slot = AccessTools.Field(typeof(SurgeryChoiceView), "slot")
                ?.GetValue(__instance) as BodyPartSlot;
            if (slot == null || !slot.HoldsMind || slot.Type.Id != "Core" || slot.IsDestroyed
                    || slot.IsPartRemoved || slot.InstalledDef == null) { return; }

            var being = slot.Body.Owner;
            if (being == null || !being.IsFriendly || being.Species == null
                    || being.Species.IsBiological || !IsResearchUnlocked()) { return; }

            var index = wFooter.Container.childCount;
            var button = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WFooterItem_DeleteButton, wFooter.Container, index);
            button.Load("Safe Core Extraction", () => {
                var op = new SurgicalOperation {
                    TargetBeing = being,
                    TargetSlot = slot,
                    OpType = (SurgicalOpType)CoreExtractionIds.SafeCoreExtraction,
                    Icon = IconId.CBodyRemoveOne,
                    Desc = "Safe Core Extraction"
                };
                if (!A.S.Query.RegisterSurgicalOperation.Ask(op, true)) {
                    A.S.Sig.ShowErrorMessage.Send(op.FailReason ?? "Could not queue safe Core extraction.");
                } else {
                    SafeCoreQueueKick.TryStartNext(being.Being);
                    A.S.UIQuery.ShowContextPopup.Ask(__instance, false);
                }
            });
            wFooter.SetActiveFast(true);
        }

        private static bool IsResearchUnlocked() {
            var progress = A.S.Research.Get(Hashes.S(CoreExtractionIds.ResearchId));
            return progress != null && progress.IsUnlocked;
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
        private static MethodInfo TargetMethod() {
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
        private static MethodInfo TargetMethod() {
            return AccessTools.GetDeclaredMethods(typeof(BodyPartSlot))
                .First(m => m.Name == "TryRemovePart" && m.GetParameters().Length == 8
                    && m.GetParameters()[7].ParameterType == typeof(string));
        }

        private static void Prefix(BodyPartSlot __instance, object[] __args) {
            if (__args == null || __args.Length != 8) { return; }
            if ((byte)__args[6] != CoreExtractionIds.SafeCoreExtraction) { return; }
            if (!__instance.HoldsMind || __instance.Type.Id != "Core") { return; }
            if (__instance.Body.Owner.Species.IsBiological || !__instance.Body.Owner.IsFriendly) { return; }
            __args[3] = false;
        }
    }
}
