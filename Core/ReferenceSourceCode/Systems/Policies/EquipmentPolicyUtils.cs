using Game.Systems.Items;

namespace Game.Systems.Policies {
    public static class EquipmentPolicyUtils {
        // The single decision point every equipment-seeking AI path goes through, so
        // AIActionFindEquipment (the fallback) can never disagree with
        // AIActionFindBestEquipment (the main path) about what a slot wants.
        //
        // equipped is the slot's current ObjData, null when the slot is empty.
        // treatAsEmpty tells the caller not to use it as the comparison baseline:
        // either there is none, or it violates the policy and should lose to
        // anything that conforms.
        public static bool SlotWantsItem(ObjData equipped, EquipmentSlotPolicy policy,
                in EquipmentPolicyFilter filter, out bool treatAsEmpty) {
            treatAsEmpty = true;
            if (policy.Mode == AutoEquipMode.Manual) {
                return false;
            }
            if (equipped == null) {
                return true;
            }
            if (!filter.Allows(equipped)) {
                // Wearing something the policy forbids. Look for a replacement no
                // matter which IncompatibleAction is set - the action only decides
                // whether the offending item also comes off before one is found.
                return true;
            }
            treatAsEmpty = false;
            // Already wearing something allowed: only Auto keeps shopping.
            return policy.Mode == AutoEquipMode.Auto;
        }

        // True when the equipped item must come off right now rather than waiting for
        // a replacement. Manual slots are the player's business and never swept, and
        // AllowsToKeep rather than Allows because only the player's toggles may evict
        // gear - a wearer's own distaste makes it shop for a replacement, above, but
        // never leaves it standing there naked.
        public static bool ShouldUnequipNow(ObjData equipped, EquipmentSlotPolicy policy,
                in EquipmentPolicyFilter filter) {
            if (equipped == null) { return false; }
            if (!policy.WantsImmediateUnequip) { return false; }
            return !filter.AllowsToKeep(equipped);
        }
    }
}
