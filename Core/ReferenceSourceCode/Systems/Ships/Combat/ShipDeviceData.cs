using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipDeviceData {
        [Key("Id")] public uint Id;
        [Key("DeviceDefId")] public string DeviceDefId;
        [Key("Health")] public float Health;
        [Key("HealthMax")] public float HealthMax;
        [Key("TileId")] public int TileId;
        [Key("CanRepairAfter")] public long CanRepairAfter;

        public static ShipDeviceData Serialize(ShipDevice device) {
            if (device.Ship.IsPlayer && device.Tile?.IsActive != true) {
                D.Err("Will not serialize player ship device without an active tile: {0}",
                    D.DumpInstance(device));
                return null;
            }
            return new ShipDeviceData {
                Id = device.Id,
                DeviceDefId = device.DeviceDef.Id,
                Health = device.Health,
                HealthMax = device.HealthMax,
                CanRepairAfter = device.CanRepairAfter,
                TileId = device.Tile != null ? device.Tile.Id : 0,
            };
        }

        public static ShipDevice TryDeserialize(GameState s, ShipDeviceData data,
                NPCShip ship) {
            try {
                if (data == null) { return null; }
                var deviceDef = ShipDeviceDef.Get(data.DeviceDefId);
                Tile tile = null;
                if (data.TileId != 0) {
                    tile = s.FindEntity<Tile>(data.TileId);
                    if (tile == null) {
                        D.Err("Tile not found by id for device: {0}",
                            D.DumpInstance(data, forLog: true));
                    }
                }
                if (tile == null && ship.IsPlayer) {
                    D.Err("Refusing to deserialize player ship device without a tile: {0}",
                        D.DumpInstance(data, forLog: true));
                    return null;
                }

                var device = new ShipDevice(data.Id, ship, deviceDef, tile) {
                    Health = data.Health,
                    HealthMax = data.HealthMax,
                    CanRepairAfter = data.CanRepairAfter,
                };

                return device;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipDeviceData for ship: {0}",
                    ship);
                return null;
            }
        }
    }
}
