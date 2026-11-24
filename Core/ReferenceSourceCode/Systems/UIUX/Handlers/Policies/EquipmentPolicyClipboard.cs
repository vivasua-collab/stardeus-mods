using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.Utils;

namespace Game.Systems.UIUX {
    // Copy / Paste on the Equipment policy tab. Holds the slot policies of whatever
    // scope was copied last, independent of which scope is on screen now.
    public static class EquipmentPolicyClipboard {
        private static readonly List<EquipmentSlotPolicy> slots = new();
        private static string sourceName;

        [ExecuteOnReload]
        private static void CleanupStatics() {
            slots.Clear();
            sourceName = null;
        }

        // Takes only the slots the source scope shows, so what gets copied is what the
        // player saw. Read-only: copying a scope that inherits never promotes it.
        public static void Copy(PolicyTabContext ctx, IReadOnlyList<InventorySlotType> types) {
            slots.Clear();
            foreach (var type in types) {
                // Effective can be the shared defaults cache or the source's live
                // policy, neither of which may end up in another scope.
                slots.Add(EquipmentPolicyScope.Effective(ctx, type).Clone());
            }
            sourceName = ctx.ScopeName;
        }

        public static bool CanPasteInto(IReadOnlyList<InventorySlotType> types) {
            foreach (var type in types) {
                if (TryGetCopied(type, out _)) { return true; }
            }
            return false;
        }

        // Overwrites only the slots the target has, so pasting between beings with
        // different slots (a human and a robot) leaves the target's other slots alone.
        // Only stores - publishing is the caller's job.
        public static bool PasteInto(PolicyTabContext ctx, IReadOnlyList<InventorySlotType> types) {
            if (!CanPasteInto(types)) { return false; }
            var target = EquipmentPolicyScope.EditablePolicy(ctx, null);
            foreach (var type in types) {
                if (TryGetCopied(type, out var copied)) {
                    // Cloned again so two scopes pasted from one copy never share an
                    // instance - editing one would silently edit the other.
                    target.SetSlot(copied.Clone());
                }
            }
            return true;
        }

        public static string PasteTip(IReadOnlyList<InventorySlotType> types) {
            var lines = new List<string> { $"{T.VerbPaste}: {Texts.Bold(sourceName)}" };
            foreach (var type in types) {
                if (!TryGetCopied(type, out var copied)) { continue; }
                lines.Add($"{type.Name}: {EquipmentPolicyTexts.Summary(copied, type)}");
            }
            return string.Join("\n", lines);
        }

        private static bool TryGetCopied(InventorySlotType type, out EquipmentSlotPolicy result) {
            foreach (var s in slots) {
                if (s.SlotIdH == type.IdH) {
                    result = s;
                    return true;
                }
            }
            result = null;
            return false;
        }
    }
}
