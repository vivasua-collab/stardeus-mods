using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Items {
    // Puts a being in a Jet Suit outright. GenItemFlags.JetSuit cannot do it: it only biases
    // the roll, and every Fly-capable suit has a SpawnRequirements.Friendlies of 8 or more,
    // which a new game or a colony with nobody left never satisfies.
    public static class JetSuits {
        // Slots come from the species, so this also answers for unspawned setup crew
        public static bool HasSuitSlot(BeingData data) {
            return data.Inventory?.Equipment.ContainsKey(InventorySlotIdH.Suit) == true;
        }

        public static bool TryEquip(GameState s, BeingData data, Rng rng) {
            if (data.Inventory == null) { return false; }
            if (!data.Inventory.Equipment.TryGetValue(InventorySlotIdH.Suit, out var slot)) {
                return false;
            }
            // A lucky SpaceReady roll can already be jet capable, in which case leave it
            if (Arrays.ContainsSafe(slot.Item?.AddsAbilities, AbilityId.Fly)) { return true; }
            var suitDef = The.Defs.FastGet(DefIdsH.ObjEquipmentClothingSpaceSuit02);
            var suit = s.Query.GenObjFull.Ask(suitDef, rng);
            if (suit == null) { return false; }
            if (data.Being == null) {
                // SpaceReady already put an ordinary suit here. Unset is the generation time
                // way to clear a slot; the discarded ObjData was never activated, so it just
                // goes away. Set() rather than Equip() because the being is not spawned.
                if (slot.Item != null) {
                    slot.Unset();
                }
                slot.Set(suit);
                slot.ApplyItemEffects();
                data.Inventory.UpdateLooks();
            } else {
                // Equipped items are saved by their object id, which generation does not
                // give; an unspawned being's gear gets one when it spawns
                // (BeingInventory.Initialize). Without it the suit was lost on every load.
                s.Query.PersistObjData.Ask(suit);
                // The jet suit comes instead of the ordinary one, so that is not dropped
                slot.Equip(suit);
                data.Inventory.RebuildLooksNow();
            }
            return true;
        }
    }
}
