using Game.Systems.Items;

namespace Game.Systems.Policies {
    // Pure resolution of the Individual -> Species -> Global -> Default chain.
    // Deliberately free of GameState so it can be unit tested without loading a game.
    public static class EquipmentPolicyResolver {
        public static EquipmentSlotPolicy Resolve(EquipmentPolicy individual,
                EquipmentPolicy species, EquipmentPolicy global, InventorySlotType type) {
            return Resolve(individual, species, global, type, out _);
        }

        public static EquipmentSlotPolicy Resolve(EquipmentPolicy individual,
                EquipmentPolicy species, EquipmentPolicy global, InventorySlotType type,
                out PolicyOrigin origin) {
            var slotIdH = type.IdH;
            if (individual != null && individual.TryGetSlot(slotIdH, out var policy)) {
                origin = PolicyOrigin.Individual;
                return policy;
            }
            if (species != null && species.TryGetSlot(slotIdH, out policy)) {
                origin = PolicyOrigin.Species;
                return policy;
            }
            if (global != null && global.TryGetSlot(slotIdH, out policy)) {
                origin = PolicyOrigin.Global;
                return policy;
            }
            origin = PolicyOrigin.Default;
            return EquipmentPolicyDefaults.DefaultFor(type);
        }

        // Which scope owns the policy as a whole, for the origin dropdown. A scope
        // owns it when it has a policy object at all, not per slot - the two-state
        // dropdown is about the whole policy.
        public static PolicyOrigin OriginOf(EquipmentPolicy individual,
                EquipmentPolicy species, EquipmentPolicy global) {
            if (individual != null) { return PolicyOrigin.Individual; }
            if (species != null) { return PolicyOrigin.Species; }
            if (global != null) { return PolicyOrigin.Global; }
            return PolicyOrigin.Default;
        }
    }
}
