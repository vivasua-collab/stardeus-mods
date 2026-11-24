using Game.CodeGen;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Items;
using KL.Randomness;

namespace Game.Systems.AI.Combat {
    /// <summary>
    /// Nobody raids weaponless: a raider that has a weapon slot always arrives with something
    /// in it, a knife at the very least. The inventory roll alone can't promise that - it
    /// filters weapons by the situation's SpawnRequirements and by the jobs the being can do,
    /// and raiders are only made melee-capable after their inventory was generated, so a
    /// pacifist roll used to leave a raider with empty hands.
    /// </summary>
    public static class RaiderWeapons {
        /// <summary>
        /// Fills an empty weapon slot: re-rolls it now that the caller has made the being
        /// capable of fighting, and falls back to a knife if nothing is eligible (a mod may
        /// gate every weapon). A filled slot, or a species without a weapon slot, is left alone.
        /// </summary>
        public static void EnsureArmed(GameState S, BeingData data, Rng rng, GenItemFlags flags) {
            var inventory = data.Inventory;
            if (inventory == null) { return; }
            if (!inventory.Equipment.TryGetValue(InventorySlotIdH.Weapon, out var slot)) {
                return;
            }
            if (slot.Item != null) { return; }
            var obj = S.Query.GenInventoryItem.Ask(rng, slot, flags | GenItemFlags.Weaponized);
            if (obj == null) {
                var knife = The.Defs.FastGet(DefIdsH.ObjEquipmentWeaponsKnife01);
                if (knife == null) { return; }
                obj = S.Query.GenObjFull.Ask(knife, rng);
                if (obj == null) { return; }
            }
            slot.Set(obj);
            slot.ApplyItemEffects();
            inventory.UpdateLooks();
        }
    }
}
