using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    // Label and summary formatting for the Equipment policy tab.
    public static class EquipmentPolicyTexts {
        // What a being's equipment policy resolves to right now.
        public static string OriginName(PoliciesSys sys, BeingData being) {
            return being?.Mind?.EquipmentPolicy != null
                ? T.PolicyOriginIndividual
                : InheritedName(sys, being);
        }

        // Where a being's values come from when it has none of its own. Names the
        // species rather than just saying "species", so the player can tell one source
        // from another at a glance.
        public static string InheritedName(PoliciesSys sys, BeingData being) {
            return sys.SpeciesEquipment(being?.Species) != null
                ? $"{T.Species} ({being.Species.Name})"
                : T.AdjDefault;
        }

        public static string ModeName(AutoEquipMode mode) {
            return mode switch {
                AutoEquipMode.Manual => T.PolicyAutoEquipManual,
                AutoEquipMode.IfEmpty => T.PolicyAutoEquipIfEmpty,
                _ => T.PolicyAutoEquipAuto,
            };
        }

        public static string WeaponKindName(WeaponKind kind) {
            return kind switch {
                WeaponKind.Melee => T.PolicyWeaponKindMelee,
                WeaponKind.Ranged => T.PolicyWeaponKindRanged,
                _ => T.PronounAny,
            };
        }

        public static string IncompatibleName(IncompatibleAction action) {
            return action == IncompatibleAction.UnequipNow
                ? T.PolicyIncompatibleUnequip
                : T.PolicyIncompatibleKeep;
        }

        public static string ColorName(string colorId) {
            if (colorId == null) { return T.PronounAny; }
            return Palette.HasColor(colorId) ? Palette.Get(colorId).NameT : T.PronounAny;
        }

        public static string ItemName(string itemDefId) {
            if (itemDefId == null) { return T.PronounAny; }
            var item = InventoryItemDef.FastGet(Hashes.S(itemDefId));
            return item?.ItemName ?? T.PronounAny;
        }

        public static string QualityRange(EquipmentSlotPolicy policy) {
            var min = Units.Percentage(policy.QualityMin);
            if (!policy.IsQualityBounded) {
                return policy.QualityMin <= 0f ? T.PronounAny : $"{min}+";
            }
            return $"{min} - {Units.Percentage(policy.EffectiveQualityMax)}";
        }

        // One-line description of a slot policy, shown on the collapsed section header.
        public static string Summary(EquipmentSlotPolicy policy, InventorySlotType type) {
            if (policy.Mode == AutoEquipMode.Manual) {
                return ModeName(policy.Mode);
            }
            var parts = new List<string> { ModeName(policy.Mode) };
            if (type.IdH == InventorySlotIdH.Weapon && policy.WeaponKind != WeaponKind.Any) {
                parts.Add(WeaponKindName(policy.WeaponKind));
            }
            if (policy.ItemDefId != null) {
                parts.Add(ItemName(policy.ItemDefId));
            }
            if (policy.ColorId != null) {
                parts.Add(ColorName(policy.ColorId));
            }
            if (policy.QualityMin > 0f || policy.IsQualityBounded) {
                parts.Add(QualityRange(policy));
            }
            // Only worn gear has an owner history. The defaults forbid dead person's
            // gear on every slot, Weapon included, so without this gate the header would
            // name a rule the section has no row for and that does nothing.
            if (type.Worn) {
                // Forbidding second-hand gear already covers anything taken off a corpse,
                // so naming both would only make the header longer. But we still show it
                if (policy.ForbidSecondHand && policy.ForbidDeadMans) {
                    parts.Add($"{T.VerbForbid}: {GlyphMap.SpriteRecycle} {GlyphMap.SpriteSkull}");
                } else if (policy.ForbidSecondHand) {
                    parts.Add($"{T.VerbForbid}: {GlyphMap.SpriteRecycle}");
                } else if (policy.ForbidDeadMans) {
                    parts.Add($"{T.VerbForbid}: {GlyphMap.SpriteSkull}");
                }
            }
            return string.Join(", ", parts);
        }

        public static string SlotIcon(InventorySlotType type) {
            return type.IdH == InventorySlotIdH.Weapon ? IconId.CKnife : IconId.CClothing;
        }
    }
}
