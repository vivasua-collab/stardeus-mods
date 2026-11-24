using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipCapacitorData {
        [Key("Device")] public ShipDeviceData Device;
        [Key("CapacitorDefId")] public string CapacitorDefId;
        [Key("Phase")] public ShipCapacitorPhase Phase;
        [Key("PhaseTicksLeft")] public int PhaseTicksLeft;
        [Key("PhaseTicksTotal")] public int PhaseTicksTotal;

        public static ShipCapacitorData Serialize(ShipCapacitor capacitor) {
            return new ShipCapacitorData {
                Device = ShipDeviceData.Serialize(capacitor.Device),
                CapacitorDefId = capacitor.CapacitorDef.Id,
                Phase = capacitor.Phase,
                PhaseTicksLeft = capacitor.PhaseTicksLeft,
                PhaseTicksTotal = capacitor.PhaseTicksTotal
            };
        }

        public static ShipCapacitor TryDeserialize(GameState s, ShipCapacitorData data, NPCShip ship) {
            try {
                var capacitorDef = ShipCapacitorDef.Get(data.CapacitorDefId);
                if (capacitorDef == null) {
                    D.Err("Skipping capacitor due to missing capacitor def id: {0}", data.CapacitorDefId);
                    return null;
                }
                var device = ShipDeviceData.TryDeserialize(s, data.Device, ship);
                if (device == null) {
                    D.Err("Skipping capacitor due to failure of deserializing the device.");
                    return null;
                }
                if (data.Phase == ShipCapacitorPhase.Undefined) {
                    data.Phase = ShipCapacitorPhase.Inactive;
                }
                var capacitor = new ShipCapacitor {
                    CapacitorDef = capacitorDef,
                    Device = device,
                    Phase = data.Phase,
                    PhaseTicksLeft = data.PhaseTicksLeft,
                    PhaseTicksTotal = data.PhaseTicksTotal
                };
                return capacitor;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipCapacitorData.");
                return null;
            }
        }
    }
}
