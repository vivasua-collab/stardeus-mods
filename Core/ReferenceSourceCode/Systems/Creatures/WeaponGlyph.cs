using Game.Input;
using Game.Systems.AI.Combat;
using Game.Systems.Items;
using Game.UI;

namespace Game.Systems.Creatures {
    // The glyph of what an attack comes from, to put in front of its damage number. A def icon
    // carries its own gap after it (DefIcons); a GlyphMap glyph gets a non-breaking space.
    public static class WeaponGlyph {
        private static string fist;
        private static string melee;
        private static string ranged;

        private static string Fist => fist ??= $"{GlyphMap.SpriteFist}\u00A0";
        private static string Melee => melee ??= $"{GlyphMap.SpriteCombat}\u00A0";
        private static string Ranged => ranged ??= $"{GlyphMap.SpriteAttackTarget}\u00A0";

        // Its weapon's icon, a fist for body parts
        public static string For(AttackMethod attack) {
            if (attack.Provider.IsBodyPart) {
                return Fist;
            }
            if (attack.Provider is InventoryItemSlot { Item: not null } slot) {
                var tag = DefIcons.Tag(slot.Item.Def);
                if (tag.Length > 0) {
                    return tag;
                }
            }
            return attack.IsMelee ? Melee : Ranged;
        }
    }
}
