using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Items {
    [MessagePackObject]
    public class InventoryData {
        [Key("Equipment")] public Dictionary<int, InventoryItemSlotData> Equipment;

        public static InventoryData Serialize(BeingInventory inv) {
            if (inv == null) {
                return null;
            }
            var d = new InventoryData();
            if (!Arrays.IsEmpty(inv.Equipment)) {
                d.Equipment = new Dictionary<int, InventoryItemSlotData>();
                foreach (var slot in inv.Equipment) {
                    d.Equipment.Add(slot.Key, InventoryItemSlotData.Serialize(slot.Value));
                }
            }
            return d;
        }

        /// <returns>false if loading was not OK, in that case being body must be generated fresh or appended to fill missing parts</returns>
        public static bool TryDeserialize(GameState s, BeingData owner, InventoryData d) {
            var inv = BeingInventory.BlankFor(s, owner);
            owner.Inventory = inv;
            if (d == null || inv == null) {
                return true;
            }
            if (Arrays.IsEmpty(d.Equipment)) {
                return true;
            }
            bool isOK = true;
            if (Arrays.Count(d.Equipment) != Arrays.Count(inv.Equipment)) {
                D.ErrSoft("There is a mismatch of saved vs native inventory count for species: {0}. Trying to load what we can. Species equipment types: {1}. Saved equipment types: {2}",
                    owner.Species, D.Dump(owner.Species.InventorySlotTypes),
                        D.Dump(d.Equipment?.Select(s => s.Value.TypeId)));
                // isOK = false;
            }

            int deserialized = 0;
            foreach (var slot in d.Equipment) {
                var ds = slot.Value;
                if (inv.Equipment.TryGetValue(slot.Key, out var invSlot)) {
                    if (InventoryItemSlotData.TryDeserialize(s, ds, invSlot)) {
                        deserialized++;
                    } else {
                        D.Err("Failed to deserialize equipment slot: {0}",
                            D.DumpInstance(ds, forLog: true));
                        isOK = false;
                    }
                } else {
                    D.ErrSoft("Could not find the slot for deserializing: {0}. Species: {1}",
                        D.DumpInstance(ds, forLog: true), owner.Species);
                        // isOK = false;
                }
            }

            if (deserialized != inv.Equipment.Count) {
                D.Err("Failed to fully deserialize all equipment parts for species {0}! Expected: {1}. Deserialized: {2}",
                    owner.Species, inv.Equipment.Count, deserialized);
                isOK = false;
            }
            return isOK;
        }

        public static BeingInventory DeepCopy(GameState S, BeingInventory inventory,
                BeingData beingData) {
            if (inventory == null) { return null; }
            var inv = BeingInventory.BlankFor(S, beingData);
            foreach (var slot in inventory.Equipment) {
                var item = slot.Value.Item;
                if (item != null) {
                    if (inv.Equipment.TryGetValue(slot.Key, out var newSlot)) {
                        var dataCopy = ObjDataData.DeepCopy(S, slot.Value.ObjData);
                        dataCopy.Id = 0;
                        dataCopy.State = ObjState.Inactive;
                        newSlot.Set(dataCopy);
                    }
                }
            }
            return inv;
        }
    }
}
