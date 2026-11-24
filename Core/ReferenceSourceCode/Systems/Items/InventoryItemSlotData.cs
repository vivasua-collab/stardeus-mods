using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Items {
    [MessagePackObject]
    public struct InventoryItemSlotData {
        [Key("TypeId")] public string TypeId;
        [Key("ItemId")] public string ItemId;
        [Key("ObjId")] public uint ObjId;

        public static InventoryItemSlotData Serialize(InventoryItemSlot slot) {
            var d = new InventoryItemSlotData();
            d.TypeId = slot.Type.Id;
            d.ItemId = slot.Item?.Id;
            d.ObjId = slot.ObjData?.Id ?? 0;
            return d;
        }

        public static bool TryDeserialize(GameState S, InventoryItemSlotData d, InventoryItemSlot slot) {
            if (d.TypeId == null) {
                return false;
            }
            if (slot.Type.Id != d.TypeId) {
                D.Err("ID mismatch in InventoryItemSlot deserialize! {0} != {1}",
                    slot.Type.Id, d.TypeId);
                return false;
            };
            if (d.ItemId != null) {
                var def = InventoryItemDef.Get(d.ItemId);
                if (def == null) {
                    D.Err("InventoryItemSlot with type {0} has missing item with def: {1}",
                        d.TypeId, d.ItemId);
                }
                if (d.ObjId == 0) {
                    D.Err("Trying to deserialize inventory item with obj id 0: {0} => {1}",
                        def, slot);
                    return false;
                }
                slot.ObjData = S.Query.FindObjData.Ask(d.ObjId);
                if (slot.ObjData == null) {
                    return false;
                }
                slot.Item = def;
                if (slot.ObjData.State != ObjState.Equipped) {
                    D.Err("Equipped object state is not != Equipped! Fixing. {0} => {1}",
                        def, slot.ObjData);
                    slot.ObjData.State = ObjState.Equipped;
                }
                if (slot.ObjData.BeingDataId == 0) {
                    D.ErrSoft("Equipped object BeingDataId == 0! Fixing. {0} => {1}",
                        def, slot.ObjData);
                    slot.ObjData.BeingDataId = slot.Inventory.Owner.Id;
                }
            }
            return true;
        }
    }
}
