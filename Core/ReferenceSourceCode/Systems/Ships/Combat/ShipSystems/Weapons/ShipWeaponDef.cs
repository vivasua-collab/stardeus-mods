using System;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using UnityEngine.VFX;

using Game.Utils;
namespace Game.Systems.Ships {
    [Serializable]
    public sealed class ShipWeaponDef {

        [ExecuteOnReload]
        private static void ClearStatics() {
            All.Clear();
        }

        [SelfInit(SelfInit.Stage.Finalize)]
        private static void Register() {
            LoadKnown();
        }
        public string Id;
        public int IdH;
        public ShipDeviceDef Device;
        public Def Def => Device.Def;
        public ShipWeaponType WeaponType;
        public string WeaponTypeName {
            get {
                switch (WeaponType) {
                    case ShipWeaponType.Ballistic: return T.WeaponTypeBalistic;
                    case ShipWeaponType.Missile: return T.WeaponTypeMissile;
                    case ShipWeaponType.Laser: return T.WeaponTypeLaser;
                    case ShipWeaponType.Plasma: return T.WeaponTypePlasma;
                    case ShipWeaponType.PointDefense: return T.WeaponTypePointdefense;
                    default: {
                        D.Err("Unhandled weapon type: {0}", WeaponType);
                        return WeaponType.ToString();
                    }
                }
            }
        }
        public string WeaponTypeId;
        public string ProjectileVFX;
        public Transform ProjectileVFXPrefab;
        public string ShotSoundFX;
        public float ShotSoundVol;
        public float ProjectileSpeed;
        public int WindupTimeMinutes;
        public int ChargeTimeMinutes;
        public int FiringTimeMinutes;
        public int SoftFluxPerShot;
        public int HardFluxPerShot;
        public int SoftFluxPerShieldHit;
        public int HardFluxPerShieldHit;
        public float Rarity => Device.Rarity;
        public int Damage;
        public string DamageTypeId;
        public int AmmoCapacity;
        public bool CreatesExplosion;
        public float ExplosionRadius;

        /// <summary>
        /// When we have a specific target on ship, this accuracy will be used
        /// to determine if we hit the target. Flat per-shot chance - not a function
        /// of anything the player controls, so there is no point sampling a curve for it.
        /// </summary>
        public float Accuracy;

        /// <summary>
        /// How likely this weapon will penetrate shields if they are up
        /// </summary>
        public float ShieldPenetrateChance;

        private float combatStrength;

        public float CombatStrength {
            get {
                if (combatStrength > 0f) {
                    return combatStrength;
                }
                if (WeaponType == ShipWeaponType.PointDefense) {
                    combatStrength = 0.1f;
                    return combatStrength;
                }
                float str = 0f;
                str += Damage * 2f;
                str += HardFluxPerShieldHit;
                // Use float division and guard against zero-cycle (instant) weapons.
                var denom = Mathf.Max(
                    (WindupTimeMinutes / 3f) + ChargeTimeMinutes + FiringTimeMinutes, 1f);
                str /= denom;
                // No Accuracy here: vs a ship it only picks the targeted device over the
                // hull, every shot that gets past Evade still lands its full damage
                var shieldPen = ShieldPenetrateChance;
                if (shieldPen > 0f) {
                    str *= 2f;
                }
                D.Warn("CombatStrength: {0} -> {1}", Id, str);
                combatStrength = str;
                return str;
            }
        }

        public float ShieldShakeAmt;

        public static Dictionary<int, ShipWeaponDef> All = new();
        public DamageType DamageType;

        public static ShipWeaponDef Get(string id) {
            if (!All.TryGetValue(Hashes.S(id), out var def)) {
                D.Err("ShipWeaponDef not found by id: {0}", id);
                D.Err("Available: {0}", string.Join(", ",
                    All.Values.Select(v => v.Id)));
                def = All.Values.FirstOrDefault();
            }
            return def;
        }
        public static ShipWeaponDef FastGet(int idH) {
            if (!All.TryGetValue(idH, out var def)) {
                D.Err("ShipWeaponDef not found by id hash: {0}", idH);
                def = All.Values.FirstOrDefault();
            }
            return def;
        }

        private static void LinkDevices() {
            List<ShipWeaponDef> broken = null;
            foreach (var w in All.Values) {
                var deviceId = $"Weapon_{w.Id}";
                w.Device = ShipDeviceDef.Get(deviceId);
                if (w.Device == null) {
                    D.Err("Weapon device data not defined for: {0}, DeviceId not found: {1}",
                        w.Id, deviceId);
                    broken ??= new();
                    broken.Add(w);
                }
            }
            if (broken != null) {
                foreach (var w in broken) {
                    All.Remove(w.IdH);
                }
                D.Err("Removed {0} broken weapon defs: {1}", broken.Count,
                    D.DumpCollection(broken));
            }
        }


        private static void LoadKnown() {
            if (All.Count > 0) { return; }
            var types = The.ModLoader.LoadObjects<ShipWeaponDef>("Config/Ships/Devices/Weapons");
            foreach (var type in types) {
                type.IdH = Hashes.S(type.Id);

                var prefab = ResourceLibrary.FindPrefab<VisualEffect>(
                    type.ProjectileVFX);
                if (prefab == null) {
                    D.Err("ShipWeaponDef projectile vfx not found: {0}: {1}",
                        type.Id, type.ProjectileVFX);
                }

                if (type.ExplosionRadius <= 0f) {
                    D.Err("ShipWeaponDef ExplosionRadius is <= 0f: {0}: {1}",
                        type.Id, type.WeaponTypeId);
                    type.ExplosionRadius = 3f;
                }

                if (!Enums.TryFromString(type.DamageTypeId, out type.DamageType)) {
                    D.Err("ShipWeaponDef DamageTypeId undefined: {0}: {1}",
                        type.Id, type.DamageTypeId);
                    type.DamageType = DamageType.Piercing;
                }
                if (!Enums.TryFromString(type.WeaponTypeId, out type.WeaponType)) {
                    D.Err("ShipWeaponDef WeaponTypeId undefined: {0}: {1}",
                        type.Id, type.WeaponTypeId);
                    type.WeaponType = ShipWeaponType.Ballistic;
                }

                if (type.Accuracy <= 0f || type.Accuracy > 1f) {
                    D.Err("ShipWeaponDef has invalid Accuracy!: {0}: {1}", type.Id, type.Accuracy);
                    type.Accuracy = 0.5f;
                }

                if (type.ShieldPenetrateChance < 0f || type.ShieldPenetrateChance > 1f) {
                    D.Err("ShipWeaponDef has invalid ShieldPenetrateChance!: {0}: {1}", type.Id, type.ShieldPenetrateChance);
                    type.ShieldPenetrateChance = 0f;
                }
                if (type.ShotSoundVol == 0f) {
                    D.Err("ShipWeaponDef has zero ShotSoundVol, setting to 1: {0}", type.Id);
                    type.ShotSoundVol = 1f;
                }

                if (type.Damage < 1) {
                    D.Err("ShipWeaponDef has Damage < 1: {0}", type.Id);
                    type.Damage = 50;
                }
                if (!All.TryAdd(type.IdH, type)) {
                    D.Err("ShipWeaponDef with such ID already exists: {0}", type.Id);
                    continue;
                }
            }
            Ready.When(WhenH.CoreLoaded2, LinkDevices);
        }

        public bool CanAddFor(NPCShip ship, int remainingCP, out float w) {
            if (Device.ClassPoints > remainingCP) {
                w = 0f;
                return false;
            }
            if (ship.Class.WeaponsTier < Device.Tier) {
                w = 0f;
                return false;
            }
            // Cap weapon tier relative to the player's arsenal (0 = unconstrained).
            if (ship.WantedWeaponsTier > 0 && Device.Tier > ship.WantedWeaponsTier) {
                w = 0f;
                return false;
            }
            w = Rarity;
            return true;
        }

        public override string ToString() {
            return $"ShipWeaponDef[{Id}]";
        }
    }
}
