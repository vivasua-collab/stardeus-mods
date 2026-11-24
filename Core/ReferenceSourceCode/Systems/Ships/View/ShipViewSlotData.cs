using System;
using Game.Constants;
using Game.Data;
using KL.Utils;
using MessagePack;
using UnityEngine;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipViewSlotData {
        [Key("Edge")] public Facing.Type Edge;
        [Key("Position")] public Vector2 Position;
        [Key("ShipId")] public uint ShipId;

        public static ShipViewSlotData Serialize(ShipViewSlot slot) {
            if (slot.View?.Ship == null) {
                D.Err("ShipSlot has now view! {0}", slot);
                return null;
            }
            return new ShipViewSlotData {
                Edge = slot.Edge,
                Position = slot.Position,
                ShipId = slot.View.Ship.Id,
            };
        }

        public static bool TryRestoreView(GameState s, ShipViewSlotData data) {
            try {
                var ship = s.Query.NPCShipById.Ask(data.ShipId);
                if (ship == null) {
                    D.Err("Can't spawn ship view when loading, ship not found for view slot: {0}",
                        D.DumpInstance(data, forLog: true));
                    return false;
                }
                s.Query.SpawnShipViewAtPos.Ask(ship, data.Edge, data.Position);
                return true;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipViewSlotData: {0}",
                    D.DumpInstance(data, forLog: true));
                return false;
            }
        }
    }
}
