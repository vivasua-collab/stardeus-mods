using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Items;

namespace Game.Systems.Policies {
    public static class EquipmentPolicyDefaults {
        // Shared read-only instances handed out by the resolver when nothing is
        // configured. Never mutate what DefaultFor returns - the UI always edits a
        // policy it owns. Rebuilt per game load so a changed tunable is picked up.
        private static readonly Dictionary<int, EquipmentSlotPolicy> cache = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            cache.Clear();
        }

        public static void Reset() {
            cache.Clear();
        }

        public static EquipmentSlotPolicy DefaultFor(InventorySlotType type) {
            if (!cache.TryGetValue(type.IdH, out var policy)) {
                policy = Create(type);
                cache[type.IdH] = policy;
            }
            return policy;
        }

        // A fresh, editable copy of the default for one slot.
        public static EquipmentSlotPolicy Create(InventorySlotType type) {
            return new EquipmentSlotPolicy {
                SlotId = type.Id,
                // Weapons are opt-in: a being never arms itself unless the player
                // says so. Everything else keeps the pre-policy behaviour.
                Mode = type.IdH == InventorySlotIdH.Weapon
                    ? AutoEquipMode.Manual
                    : AutoEquipMode.Auto,
                QualityMin = 0f,
                QualityMax = EquipmentSlotPolicy.UnboundedQuality,
                ColorId = null,
                ItemDefId = null,
                WeaponKind = WeaponKind.Any,
                ForbidWhenDropped = Tunables.AutoForbidDroppedWeapons,
                Incompatible = IncompatibleAction.KeepUntilReplaced,
                // Hand-me-downs stay allowed out of the box: the mood hit is the
                // cost, refusing them outright is the player's choice. Gear off a
                // corpse is refused unless the player opts in. Set on every slot,
                // Weapon included - the filter only acts on it through
                // ItemOwnership.IsDeadMansGear, which is inert off Worn slots.
                ForbidSecondHand = false,
                ForbidDeadMans = true,
            };
        }

        // A full, editable policy covering every known slot type.
        public static EquipmentPolicy CreateDefault() {
            var types = InventorySlotType.All;
            var policy = new EquipmentPolicy();
            policy.Slots = new EquipmentSlotPolicy[types.Count];
            int i = 0;
            foreach (var type in types) {
                policy.Slots[i++] = Create(type);
            }
            return policy;
        }
    }
}
