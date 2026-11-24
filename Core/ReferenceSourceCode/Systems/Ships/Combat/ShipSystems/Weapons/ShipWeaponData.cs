using System;
using Game.Data;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public class ShipWeaponData {
        [Key("Device")] public ShipDeviceData Device;
        [Key("WeaponDefId")] public string WeaponDefId;
        [Key("Phase")] public ShipWeaponPhase Phase;
        [Key("PhaseTicksLeft")] public int PhaseTicksLeft;
        [Key("PhaseTicksTotal")] public int PhaseTicksTotal;
        [Key("Ammo")] public int Ammo;
        [Key("Targets")] public (uint, uint, int) Targets;
        [Key("Stats")] public ShipWeaponStatsData Stats;
        [Key("Settings")] public ShipWeaponSettingsData Settings;
        [Key("UseSettings")] public bool UseSettings;
        [Key("ReloadTick")] public long NextReloadTick;

        public static ShipWeaponData Serialize(ShipWeapon weapon) {
            if (weapon.Phase == ShipWeaponPhase.Undefined) {
                weapon.Phase = ShipWeaponPhase.Inactive;
            }
            return new ShipWeaponData {
                Device = ShipDeviceData.Serialize(weapon.Device),
                WeaponDefId = weapon.WeaponDef.Id,
                Phase = weapon.Phase,
                PhaseTicksLeft = weapon.PhaseTicksLeft,
                PhaseTicksTotal = weapon.PhaseTicksTotal,
                NextReloadTick = weapon.NextReloadTick,
                Ammo = weapon.Ammo,
                Targets = (
                    weapon.TargetShip?.Id ?? 0,
                    weapon.Target?.Id ?? 0,
                    weapon.NonShipTarget?.Entity?.Id ?? 0
                ),
                Stats = ShipWeaponStatsData.Serialize(weapon.Stats),
                Settings = ShipWeaponSettingsData.Serialize(weapon.Settings),
                UseSettings = weapon.UseSettings
            };
        }

        public static ShipWeapon TryDeserialize(GameState s, ShipWeaponData data,
                NPCShip ship) {
            try {
                var weaponDef = ShipWeaponDef.Get(data.WeaponDefId);
                if (weaponDef == null) {
                    D.Err("Skipping weapon due to missing weapon def id: {0}: {1}",
                        data.WeaponDefId,
                        D.DumpInstance(data, forLog: true));
                    return null;
                }
                var device = ShipDeviceData.TryDeserialize(s, data.Device, ship);
                if (device == null) {
                    D.Err("Skipping weapon due to failure of deserializing the device: {0}",
                        D.DumpInstance(data, forLog: true));
                    return null;
                }
                if (data.Phase == ShipWeaponPhase.Undefined) {
                    data.Phase = ShipWeaponPhase.Inactive;
                }
                var weapon = new ShipWeapon {
                    WeaponDef = weaponDef,
                    Device = device,
                    Phase = data.Phase,
                    PhaseTicksLeft = data.PhaseTicksLeft,
                    PhaseTicksTotal = data.PhaseTicksTotal,
                    NextReloadTick = data.NextReloadTick,
                    Ammo = data.Ammo,
                    TargetData = data.Targets,
                    Stats = ShipWeaponStatsData.Deserialize(data.Stats),
                    Settings = ShipWeaponSettingsData.Deserialize(data.Settings),
                    UseSettings = data.UseSettings
                };

                return weapon;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed to deserialize ShipWeaponData for ship: {0}",
                    ship, D.DumpInstance(data, forLog: true));
                return null;
            }
        }
    }
}
