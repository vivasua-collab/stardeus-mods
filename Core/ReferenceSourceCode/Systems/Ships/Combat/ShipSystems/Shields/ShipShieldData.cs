using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipShieldData {
        [Key("Device")] public ShipDeviceData Device;
        [Key("ShieldDefId")] public string ShieldDefId;
        [Key("Phase")] public ShipShieldPhase Phase;
        [Key("PhaseTicksLeft")] public int PhaseTicksLeft;
        [Key("PhaseTicksTotal")] public int PhaseTicksTotal;

        public static ShipShieldData Serialize(ShipShield shield) {
            return new ShipShieldData {
                Device = ShipDeviceData.Serialize(shield.Device),
                ShieldDefId = shield.ShieldDef.Id,
                Phase = shield.Phase,
                PhaseTicksLeft = shield.PhaseTicksLeft,
                PhaseTicksTotal = shield.PhaseTicksTotal
            };
        }

        public static ShipShield TryDeserialize(GameState s, ShipShieldData data, NPCShip ship) {
            try {
                var shieldDef = ShipShieldDef.Get(data.ShieldDefId);
                if (shieldDef == null) {
                    D.Err("Skipping shield due to missing shield def id: {0}: {1}",
                        data.ShieldDefId,
                        D.DumpInstance(data, forLog: true));
                    return null;
                }
                var device = ShipDeviceData.TryDeserialize(s, data.Device, ship);
                if (device == null) {
                    D.Err("Skipping shield due to failure of deserializing the device: {0}",
                        D.DumpInstance(data, forLog: true));
                    return null;
                }
                if (data.Phase == ShipShieldPhase.Undefined) {
                    data.Phase = ShipShieldPhase.Inactive;
                }
                var shield = new ShipShield {
                    ShieldDef = shieldDef,
                    Phase = data.Phase,
                    PhaseTicksLeft = data.PhaseTicksLeft,
                    PhaseTicksTotal = data.PhaseTicksTotal,
                    Device = device
                };

                return shield;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipShieldData for ship: {0}",
                    ship, D.DumpInstance(data, forLog: true));
                return null;
            }
        }
    }
}
