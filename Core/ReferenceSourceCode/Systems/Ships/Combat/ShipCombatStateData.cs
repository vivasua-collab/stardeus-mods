using System;
using System.Collections.Generic;
using Game.Data;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using MessagePack;

namespace Game.Systems.Ships {
    [MessagePackObject]
    public sealed class ShipCombatStateData {
        [Key("Flux")] public ShipFluxData Flux;
        [Key("Weapons")] public List<ShipWeaponData> Weapons;
        [Key("Shields")] public List<ShipShieldData> Shields;
        [Key("Engines")] public List<ShipEngineData> Engines;
        [Key("Capacitors")] public List<ShipCapacitorData> Capacitors;
        [Key("Vents")] public List<ShipVentData> Vents;
        [Key("LastDeviceId")] public uint LastDeviceId;
        [Key("HP")] public float HP;
        [Key("MaxHP")] public int MaxHP;
        [Key("Rng")] public uint RngState;
        [Key("NumShields")] public int NumShields;
        [Key("NumShieldsUp")] public int NumShieldsUp;
        [Key("WeaponSettings")] public ShipWeaponSettingsData WeaponSettings;
        [Key("FluxSettings")] public ShipFluxSettingsData FluxSettings;
        [Key("FluxStrategy")] public string FluxStrategyId;
        [Key("WeaponStrategy")] public string WeaponStrategyId;
        [Key("TargetStrategy")] public string TargetStrategyId;
        [Key("NextTargetSwitchTick")] public long NextTargetSwitchTick;
        /// <summary>
        /// Ship id of the current target, 0 when there is none
        /// </summary>
        [Key("FocusedEnemyId")] public uint FocusedEnemyId;
        [Key("TicksTillShoot")] public int TicksTillShoot;
        [Key("NextNPCFluxBlunderAt")] public long NextNPCFluxBlunderAt;
        [Key("BreachCapsulesSent")] public int BreachCapsulesSent;
        [Key("NextBreachCapsuleTick")] public long NextBreachCapsuleTick;
        [Key("LastAttackerId")] public uint LastAttackerId;
        /// <summary>
        /// Weapon def id -> reserve ammunition. Null (not empty) in saves made before
        /// reserve ammunition was serialized.
        /// </summary>
        [Key("AmmoStock")] public Dictionary<string, int> AmmoStock;

        public static ShipCombatStateData Serialize(NPCShip ship) {
            try {
                if (ship.CombatState == null) {
                    D.Err("ShipCombatState was not set for ship: {0}", ship);
                    ship.CombatState = new ShipCombatState(ship);
                }
                var cs = ship.CombatState;
                cs.ValidatePlayerDevices();
                var data = new ShipCombatStateData {
                    Flux = ShipFluxData.Serialize(cs.Flux),
                    Weapons = cs.Weapons.ConvertAll(ShipWeaponData.Serialize),
                    Shields = cs.Shields.ConvertAll(ShipShieldData.Serialize),
                    Engines = cs.Engines.ConvertAll(ShipEngineData.Serialize),
                    LastDeviceId = cs.LastDeviceId,
                    RngState = cs.Rng.State,
                    HP = cs.HP,
                    MaxHP = cs.MaxHP,
                    NumShieldsUp = cs.NumShieldsUp,
                    WeaponSettings = ShipWeaponSettingsData.Serialize(cs.WeaponSettings),
                    FluxSettings = ShipFluxSettingsData.Serialize(cs.FluxSettings),
                    FluxStrategyId = cs.StrategyFlux?.Id,
                    WeaponStrategyId = cs.StrategyWeapon?.Id,
                    TargetStrategyId = cs.StrategyTarget?.Id,
                    NextTargetSwitchTick = cs.NextTargetSwitchTick,
                    FocusedEnemyId = cs.FocusedEnemy?.Id ?? 0,
                    TicksTillShoot = cs.TicksTillShoot,
                    NextNPCFluxBlunderAt = cs.NextNPCFluxBlunderAt,
                    BreachCapsulesSent = cs.BreachCapsulesSent,
                    NextBreachCapsuleTick = cs.NextBreachCapsuleTick,
                    LastAttackerId = cs.LastAttacker?.Id ?? 0,
                    AmmoStock = SerializeAmmoStock(cs),
                    Capacitors = cs.Capacitors.ConvertAll(ShipCapacitorData.Serialize),
                    Vents = cs.Vents.ConvertAll(ShipVentData.Serialize),
                };
                return data;
            } catch (Exception ex) {
                D.LogEx(ex, "Failed serializing ship combat state: {0}: {1}",
                    ship, D.DumpInstance(ship.CombatState, forLog: true));
                return null;
            }
        }

        private static Dictionary<string, int> SerializeAmmoStock(ShipCombatState cs) {
            var stock = new Dictionary<string, int>();
            foreach (var kv in cs.AmmoStock) {
                stock[kv.Key.Id] = kv.Value;
            }
            return stock;
        }

        private static void RestoreAmmoStock(ShipCombatStateData data, ShipCombatState cs,
                NPCShip ship) {
            if (data.AmmoStock == null) {
                // Legacy save: reserve stock was never written, so it has to be
                // regenerated. Only for ships that are actually fighting, and off a
                // clone of the combat Rng, so restored weapon state stays exact.
                if (ship.IsInCombat && !ship.IsPlayer) {
                    cs.GenerateAmmoStock(Rng.Load(cs.Rng.State).Fork());
                }
                return;
            }
            foreach (var kv in data.AmmoStock) {
                if (!ShipWeaponDef.All.TryGetValue(Hashes.S(kv.Key), out var def)) {
                    D.Err("Dropping ammo stock of an unknown weapon def: {0}", kv.Key);
                    continue;
                }
                cs.AmmoStock[def] = kv.Value;
            }
        }

        public static bool TryDeserialize(GameState s, ShipCombatStateData data, NPCShip ship) {
            if (data == null) {
                if (ship.IsPlayer) {
                    D.Err("Player ship combat data is null!");
                    ShipCombatState.CreateFor(ship);
                }
                return true;
            }
            try {
                var flux = ShipFluxData.TryDeserialize(s, data.Flux, ship);
                var cs = new ShipCombatState(ship, flux) {
                    LastDeviceId = data.LastDeviceId,
                    HP = data.HP,
                    Rng = Rng.Load(data.RngState),
                    NumShieldsUp = data.NumShieldsUp,
                    WeaponSettings = ShipWeaponSettingsData.Deserialize(data.WeaponSettings),
                    FluxSettings = ShipFluxSettingsData.Deserialize(data.FluxSettings),
                    StrategyFlux = ShipCombatFluxStrategyDef.TryGet(data.FluxStrategyId),
                    StrategyWeapon = ShipCombatWeaponStrategyDef.TryGet(data.WeaponStrategyId),
                    StrategyTarget = ShipCombatTargetStrategyDef.TryGet(data.TargetStrategyId),
                    NextTargetSwitchTick = data.NextTargetSwitchTick,
                    FocusedEnemyId = data.FocusedEnemyId,
                    NextNPCFluxBlunderAt = data.NextNPCFluxBlunderAt,
                    TicksTillShoot = data.TicksTillShoot,
                    BreachCapsulesSent = data.BreachCapsulesSent,
                    NextBreachCapsuleTick = data.NextBreachCapsuleTick,
                    LastAttackerId = data.LastAttackerId,
                };
                cs.RecalculateMaxHP(capCurrentHP: true);
                if (cs.WeaponSettings == null && ship.IsPlayer) {
                    cs.WeaponSettings = ShipWeaponSettings.Default;
                }
                if (cs.FluxSettings == null && ship.IsPlayer) {
                    cs.FluxSettings = ShipFluxSettings.Default;
                }

                if (data.Weapons != null) {
                    foreach (var weaponData in data.Weapons) {
                        var weapon = ShipWeaponData.TryDeserialize(s, weaponData, ship);
                        if (weapon != null) {
                            cs.AddDevice(weapon);
                        }
                    }
                }

                if (data.Shields != null) {
                    foreach (var shieldData in data.Shields) {
                        var shield = ShipShieldData.TryDeserialize(s, shieldData, ship);
                        if (shield != null) {
                            cs.AddDevice(shield);
                        }
                    }
                }

                if (data.Engines != null) {
                    foreach (var engineData in data.Engines) {
                        var engine = ShipEngineData.TryDeserialize(s, engineData, ship);
                        if (engine != null) {
                            cs.AddDevice(engine);
                        }
                    }
                }

                if (data.Capacitors != null) {
                    foreach (var capacitorData in data.Capacitors) {
                        var capacitor = ShipCapacitorData.TryDeserialize(s, capacitorData, ship);
                        if (capacitor != null) {
                            cs.AddDevice(capacitor);
                        }
                    }
                    cs.Flux.UpdateCapacity(cs);
                }

                if (data.Vents != null) {
                    foreach (var ventData in data.Vents) {
                        var vent = ShipVentData.TryDeserialize(s, ventData, ship);
                        if (vent != null) {
                            cs.AddDevice(vent);
                        }
                    }
                    cs.Flux.UpdateCapacity(cs);
                }
                // needs the weapons to be in place
                RestoreAmmoStock(data, cs, ship);
                ship.CombatState = cs;

                return true;
            } catch (Exception ex) {
                ship.CombatState = null;
                D.LogEx(ex, "Failed deserializing ship combat state data: {0}: {1}",
                    ship, D.DumpInstance(data, forLog: true));
                return false;
            }
        }
    }
}
