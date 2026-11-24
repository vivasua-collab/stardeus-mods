using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipVentData {
        [Key("Device")] public ShipDeviceData Device;
        [Key("VentDefId")] public string VentDefId;

        public static ShipVentData Serialize(ShipVent vent) {
            return new ShipVentData {
                Device = ShipDeviceData.Serialize(vent.Device),
                VentDefId = vent.VentDef.Id
            };
        }

        public static ShipVent TryDeserialize(GameState s, ShipVentData data, NPCShip ship) {
            try {
                var ventDef = ShipVentDef.Get(data.VentDefId);
                if (ventDef == null) {
                    D.Err("Skipping vent due to missing vent def id: {0}", data.VentDefId);
                    return null;
                }
                var device = ShipDeviceData.TryDeserialize(s, data.Device, ship);
                if (device == null) {
                    D.Err("Skipping vent due to failure of deserializing the device.");
                    return null;
                }
                var vent = new ShipVent {
                    VentDef = ventDef,
                    Device = device
                };
                return vent;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipVentData.");
                return null;
            }
        }
    }
}
