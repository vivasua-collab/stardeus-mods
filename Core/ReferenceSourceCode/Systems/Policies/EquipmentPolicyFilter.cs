using Game.Constants;
using Game.Systems.Creatures;
using Game.Systems.Items;
using UnityEngine;

namespace Game.Systems.Policies {
    // Per-candidate item predicate for one slot policy. Build it once per query and
    // reuse it across the candidate loop - all string and palette lookups happen in
    // the constructor, never per item.
    public readonly struct EquipmentPolicyFilter {
        private readonly int itemDefIdH;
        private readonly float qualityMin;
        private readonly float qualityMax;
        private readonly bool boundedQuality;
        private readonly WeaponKind weaponKind;
        private readonly Color color;
        private readonly bool hasColor;
        // The wearer, reduced in the constructor to what the ownership rules actually
        // need, so nothing heavier than four primitives is stored per query.
        private readonly bool forbidSecondHand;
        private readonly bool forbidDeadMans;
        // The wearer's own opinion, kept apart from the two policy toggles above: it
        // decides what the being will pick up, never what may be taken off it.
        private readonly bool refusesUsedGear;
        private readonly uint wearerId;
        private readonly uint wearerMindId;

        // Always takes the being the gear is for: the second-hand rules are relative
        // to the wearer (their own clothes are not second-hand to them), and a being
        // that cannot care must ignore both toggles.
        public static EquipmentPolicyFilter For(EquipmentSlotPolicy policy, BeingData wearer) {
            if (policy == null) {
                // No policy to apply (e.g. a search that isn't slot specific).
                // A default-constructed filter allows everything.
                return default;
            }
            Color resolved = default;
            // A colour the palette no longer knows (removed mod) degrades to "any"
            // rather than making the slot unfillable.
            bool hasColor = policy.ColorId != null
                && Palette.TryColorById(policy.ColorId, out resolved);
            if (!ItemOwnership.CaresAboutOrigin(wearer)) {
                return new EquipmentPolicyFilter(policy, hasColor, resolved, 0, 0, false, false);
            }
            return new EquipmentPolicyFilter(policy, hasColor, resolved,
                wearer.Id, wearer.Mind?.Id ?? 0, true,
                ItemOwnership.RefusesUsedGear(wearer));
        }

        // Colour already resolved, no wearer. Lets callers (and tests) skip the
        // palette; the ownership rules are inert without a wearer to judge against.
        public static EquipmentPolicyFilter Create(EquipmentSlotPolicy policy, Color? color) {
            return new EquipmentPolicyFilter(policy, color.HasValue,
                color ?? default, 0, 0, false, false);
        }

        private EquipmentPolicyFilter(EquipmentSlotPolicy policy, bool hasColor, Color color,
                uint wearerId, uint wearerMindId, bool caresAboutOrigin, bool refusesUsedGear) {
            itemDefIdH = policy.ItemDefIdH;
            qualityMin = policy.QualityMin;
            qualityMax = policy.QualityMax;
            boundedQuality = policy.IsQualityBounded;
            weaponKind = policy.WeaponKind;
            this.hasColor = hasColor;
            this.color = color;
            this.wearerId = wearerId;
            this.wearerMindId = wearerMindId;
            forbidSecondHand = caresAboutOrigin && policy.ForbidSecondHand;
            forbidDeadMans = caresAboutOrigin && policy.ForbidDeadMans;
            this.refusesUsedGear = refusesUsedGear;
        }

        // What the being will put on: the policy, plus whatever the wearer refuses on
        // its own. Every AI path that goes shopping asks this one.
        public bool Allows(ObjData objData) {
            return IsAllowed(objData, refusesUsedGear);
        }

        // What the being may keep wearing. Deliberately blind to the wearer's own
        // refusal: a trait says what someone will pick up, and stripping a Fastidious
        // colonist naked to spare them a used shirt trades one grievance for a worse
        // one. Only the player's toggles evict gear.
        public bool AllowsToKeep(ObjData objData) {
            return IsAllowed(objData, false);
        }

        private bool IsAllowed(ObjData objData, bool wearerRefusesUsed) {
            if (objData == null) { return false; }
            if ((forbidDeadMans || wearerRefusesUsed)
                    && ItemOwnership.IsDeadMansGear(objData)) {
                return false;
            }
            if ((forbidSecondHand || wearerRefusesUsed)
                    && ItemOwnership.IsSecondHandFor(objData, wearerId, wearerMindId)) {
                return false;
            }
            return Allows(objData.Item, objData.Quality, objData.Color);
        }

        public bool Allows(InventoryItemDef item, float quality, Color itemColor) {
            if (item == null) { return false; }
            if (itemDefIdH != 0 && item.IdH != itemDefIdH) { return false; }
            if (quality < qualityMin) { return false; }
            if (boundedQuality && quality > qualityMax) { return false; }
            if (weaponKind != WeaponKind.Any && item.Kind != weaponKind) { return false; }
            if (hasColor && itemColor != color) { return false; }
            return true;
        }
    }
}
