using System;
using Game.Systems.Items;
using MessagePack;

namespace Game.Systems.Policies {
    [MessagePackObject]
    public sealed class EquipmentPolicy {
        // One entry per inventory slot type. Vanilla has 5, so a linear scan is
        // cheaper than a dictionary and keeps the save free of hashed keys.
        [Key("Slots")] public EquipmentSlotPolicy[] Slots;

        public bool TryGetSlot(int slotIdH, out EquipmentSlotPolicy result) {
            if (Slots != null) {
                for (int i = 0; i < Slots.Length; i++) {
                    EquipmentSlotPolicy s = Slots[i];
                    if (s != null && s.SlotIdH == slotIdH) {
                        result = s;
                        return true;
                    }
                }
            }
            result = null;
            return false;
        }

        // Used by the UI when the player edits a slot that this policy doesn't
        // cover yet (e.g. a slot type added by a mod after the policy was saved).
        public EquipmentSlotPolicy GetOrAdd(InventorySlotType type) {
            if (TryGetSlot(type.IdH, out var existing)) {
                return existing;
            }
            var added = EquipmentPolicyDefaults.Create(type);
            int idx = Slots?.Length ?? 0;
            if (Slots == null) {
                Slots = new EquipmentSlotPolicy[1];
            } else {
                Array.Resize(ref Slots, idx + 1);
            }
            Slots[idx] = added;
            return added;
        }

        // Replaces the entry for slot's type, or appends it when this policy doesn't
        // cover that type yet.
        public void SetSlot(EquipmentSlotPolicy slot) {
            if (Slots != null) {
                for (int i = 0; i < Slots.Length; i++) {
                    if (Slots[i] != null && Slots[i].SlotIdH == slot.SlotIdH) {
                        Slots[i] = slot;
                        return;
                    }
                }
            }
            int idx = Slots?.Length ?? 0;
            if (Slots == null) {
                Slots = new EquipmentSlotPolicy[1];
            } else {
                Array.Resize(ref Slots, idx + 1);
            }
            Slots[idx] = slot;
        }

        public EquipmentPolicy Clone() {
            var copy = new EquipmentPolicy();
            if (Slots != null) {
                copy.Slots = new EquipmentSlotPolicy[Slots.Length];
                for (int i = 0; i < Slots.Length; i++) {
                    copy.Slots[i] = Slots[i]?.Clone();
                }
            }
            return copy;
        }

        public override string ToString() {
            return $"EquipmentPolicy[{Slots?.Length ?? 0}]";
        }
    }
}
