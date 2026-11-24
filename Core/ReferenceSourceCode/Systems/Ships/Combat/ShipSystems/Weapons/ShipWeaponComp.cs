using Game.Data;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using Game.Components;
using Game.Components.Upgrades;
using KL.Collections;
using Game.Systems.Ships;
using Game.Systems.Turrets;
using System.Collections.Generic;
using Game.Constants;
using System;
using Game.Utils;
using System.Text;
using Game.Input;
using Game.Systems.Notify;
using Game.Systems.Stats;
using Game.Systems.UIUX;
using Game.UI;

namespace Game.Systems.Ships {
    public sealed class ShipWeaponComp : BaseComponent<ShipWeaponComp>,
            IShipDeviceComp, IDefDescriptionProvider, ICopyableComp, ISpeedUpgradeable,
            IStatProvider, IDefPowerProvider {

        private enum WarningType {
            None,
            NoStation,
            NeedMoreStations,
        }

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipWeaponComp());
        }
        private RotatingTurretComp turretComp;
        private RotatingTurret turret;
        public RotatingTurret Turret => turret;
        private ShipWeapon weapon;
        public ShipWeapon Weapon => weapon;
        private NotificationHandle noStationNotification;
        private ShipWeaponDef weaponDef;
        public ShipDeviceDef DeviceDef => weaponDef.Device;
        private RefillableStorageComp ammo;
        public RefillableStorageComp Ammo => ammo;
        public bool IsCopyable => weapon != null;
        public string CopyConfigText => T.NounShipWeapon;
        private bool isRegistered;
        private uint shipDeviceId;
        private WarningType currentWarning;
        private float upgradeSpeed = 1f;
        public float UpgradeSpeed => upgradeSpeed;
        public bool IsSpeedUpgradeable => true;

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
            if (upgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} -> {1}", amt, upgradeSpeed);
                upgradeSpeed = 1f;
            }
        }

        public string StatsCategoryName => T.NounShipWeapon;
        public int StatsOrder => 15;

        /// <summary>
        /// Windup/Cooldown are shortened by Speed upgrades at runtime
        /// (see ShipWeapon.SetPhase), mirror that here for display.
        /// </summary>
        private string EffTime(int defMinutes) {
            return Units.TicksToTime(Mathf.Max(1, Mathf.RoundToInt(defMinutes / upgradeSpeed)), true);
        }

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (weaponDef == null) { return; }
            var cp = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            cp.Load(IconId.CCombat, T.NounClassPoints, Units.Num(weaponDef.Device.ClassPoints));

            var type = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            type.Load(IconId.CCombat, T.NounWeaponType, weaponDef.WeaponTypeName);

            var power = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            power.Load(IconId.CCombat, T.NounWeaponPower, Units.Num((int) weaponDef.CombatStrength));

            var dmg = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            dmg.Load(IconId.CCombat, T.NounDamage,
                $"{Units.Num(weaponDef.Damage)} ({DamageTypes.NameFor(weaponDef.DamageType)})");

            // var accuracy = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            // accuracy.Load(IconId.CTarget, T.StatAccuracy, Units.PercentageWithFraction1(weaponDef.Accuracy));

            // var shieldPen = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            // shieldPen.Load(IconId.CShield, T.StatShieldPenetrateChance,
            //     Units.PercentageWithFraction1(weaponDef.ShieldPenetrateChance));

            var windup = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            windup.Load(IconId.CTime, T.StatWindupTime, EffTime(weaponDef.WindupTimeMinutes));

            var cooldown = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            cooldown.Load(IconId.CTime, T.StatChargeTime, EffTime(weaponDef.ChargeTimeMinutes));

            var firing = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            firing.Load(IconId.CTime, T.StatFiringTime,
                Units.TicksToTime(weaponDef.FiringTimeMinutes, true));

            if (weaponDef.AmmoCapacity > 0) {
                var ammoCap = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                ammoCap.Load(IconId.CAmmo, T.AmmoCapacity, Units.Num(weaponDef.AmmoCapacity));
            }

            AddFluxStat(life, block, ref idx, T.StatFluxPerShotHard, weaponDef.HardFluxPerShot,
                T.StatFluxPerShotDesc_V1.F(T.FluxHard));
            AddFluxStat(life, block, ref idx, T.StatFluxPerShotSoft, weaponDef.SoftFluxPerShot,
                T.StatFluxPerShotDesc_V1.F(T.FluxSoft));
            AddFluxStat(life, block, ref idx, T.StatFluxPerHitHard, weaponDef.HardFluxPerShieldHit,
                T.StatFluxPerHitDesc_V2.F(T.FluxHard, T.NounShields));
            AddFluxStat(life, block, ref idx, T.StatFluxPerHitSoft, weaponDef.SoftFluxPerShieldHit,
                T.StatFluxPerHitDesc_V2.F(T.FluxSoft, T.NounShields));

            var energy = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            energy.Load(IconId.CEnergy, $"{T.EnergyConsumption} ({T.Max})", Units.Electricity(weaponDef.Device.EnergyCostActive));
        }

        private static void AddFluxStat(Lifetime life, WGroup block, ref int idx,
                string label, int amount, string tip) {
            if (amount <= 0) { return; }
            var row = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            row.Load(IconId.CFlux, label, Units.Num(amount));
            UIUtils.AddTip(life, row, tip);
        }

        public void SetTarget(ITurretTarget target) {
            if (turret != null) {
                turret.Target = target;
            }
            if (weapon != null) {
                if (target is IShipCombatTurretTarget ttarget) {
                    weapon.NonShipTarget = ttarget;
                } else {
                    weapon.NonShipTarget = null;
                }
            } else {
                D.Err("ShipWeapon is null, cannot set target: {0} | {1}",
                    target, Tile);
            }
        }

        protected override void OnConfig() {
            var id = Config.GetString(PropertyIdH.ShipWeaponId);
            weaponDef = ShipWeaponDef.Get(id);
            if (weaponDef == null) {
                D.Err("Cannot find ShipWeaponDef by id {0} for {1}",
                    id, entity.DefinitionId);
            }
        }
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (Tile.IsConstructed && !isRegistered
                    && EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                RegisterWeapon(true);
            }
        }

        private void RestoreDevice() {
            if (shipDeviceId != 0) {
                var device = S.Query.FindShipDevice.Ask(
                    S.Universe.Player.Ship, shipDeviceId);
                if (device is ShipWeapon w) {
                    if (w.Device.Tile != Tile) {
                        D.Err("Weapon device tile mismatch! {0}. Tile = {1}, Expected = {2}",
                            w, Tile, w.Device.Tile);
                        if (w.Device.Tile != null) {
                            return;
                        } else {
                            w.Device.SetTile(Tile);
                        }
                    }
                    weapon = w;
                    weapon.OnSetTarget = OnSetTarget;
                } else {
                    D.Err("Failed to restore ShipDevice for: {0}. Not a ShipWeapon: {1}",
                        Tile, device);
                }
            }
            RegisterWeapon(true, wasLoaded: true);
        }


        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed && !isRegistered) {
                if (wasLoaded) {
                    Ready.When(WhenH.ShipDataRestored, RestoreDevice);
                } else {
                    RegisterWeapon(true);
                }
            }
            entity.TryGetComponent(out ammo);
            if (entity.TryGetComponent(out turretComp)) {
                turret = turretComp.Turret;
                turret.Type |= TurretType.ShipCombat;
                turret.TryShoot = TryShoot;
                turret.CheckTarget = CheckTarget;
                turret.SetTarget = SetTarget;
                turret.OnTick = TickTurret;
                LinkWeapon();
            }
        }

        private void LinkWeapon() {
            if (turret == null || weapon == null) { return; }
            turret.ShipWeapon = weapon;
            weapon.OnSetTarget = OnSetTarget;
            if (turret.ShotFXPrefab != weapon.WeaponDef.ProjectileVFX) {
                if (turret.ShotFXPrefab != null) {
                    D.Err("ShipWeapon RotatingTurret shot fx prefab is set but does not match ShipWeapon: {0}: {1} != {2}",
                        entity.Definition,
                        turret.ShotFXPrefab,
                        weapon.WeaponDef.ProjectileVFX);
                }
                turret.ShotFXPrefab = weapon.WeaponDef.ProjectileVFX;
            }
            if (turret.ShotSound != weapon.WeaponDef.ShotSoundFX) {
                if (turret.ShotSound != null) {
                    D.Err("ShipWeapon RotatingTurret shot sound fx is set but does not match ShipWeapon: {0}: {1} != {2}",
                        entity.Definition,
                        turret.ShotSound,
                        weapon.WeaponDef.ShotSoundFX);
                }
                turret.ShotSound = weapon.WeaponDef.ShotSoundFX;
            }
            if (turret.ShotSoundVol != weapon.WeaponDef.ShotSoundVol) {
                if (turret.ShotSoundVol != 1f) {
                    D.Err("ShipWeapon RotatingTurret shot sound vol is set but does not match ShipWeapon: {0}: {1} != {2}",
                        entity.Definition,
                        turret.ShotSoundVol,
                        weapon.WeaponDef.ShotSoundVol);
                }
                turret.ShotSoundVol = weapon.WeaponDef.ShotSoundVol;
            }
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("ShipDeviceId", weapon?.Device.Id ?? 0);
        }

        protected override void OnLoad(ComponentData data) {
            shipDeviceId = data.GetUInt("ShipDeviceId", 0);
        }

        private void RegisterWeapon(bool add, bool wasLoaded = false) {
            if (isRegistered == add) { return; }
            // D.Warn("Registering weapon: {0}", add);
            if (add && !Tile.IsActive) { return; }
            if (add && Tile.Reinstallable.IsMidRelocation) { return; }
            if (add && !EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                D.Warn("Skipping adding weapon to tile outside of margin: {0}",
                    Tile);
                return;
            }
            var ship = S.Universe.Player.Ship;
            if (add) {
                if (weapon == null) {
                    // create + add
                    weapon = ship.CombatState.AddWeapon(weaponDef);
                    if (!weapon.Device.SetTile(Tile)) {
                        ship.CombatState.RemoveDevice(weapon);
                        isRegistered = false;
                        return;
                    }
                    if (wasLoaded) {
                        D.Err("Loaded weapon but it was null, recreated: {0}",
                            weapon);
                    }
                } else {
                    if (wasLoaded) {
                        if (!ship.CombatState.Weapons.Contains(weapon)) {
                            ship.CombatState.AddDevice(weapon);
                            D.Err("Loaded weapon but it wasn't in combat state, adding: {0}",
                                weapon);
                        }
                    } else {
                        // readd
                        if (!ship.CombatState.AddDevice(weapon)) {
                            D.Err("Trying to add weapon twice: {0}", weapon);
                        }
                    }
                }
                LinkWeapon();
                weapon.OnPhaseChange = OnPhaseChange;
                weapon.OnPhaseProgressChange = OnPhaseProgress;
                weapon.Comp = this;
                OnPhaseChange();
                OnPhaseProgress();
            } else {
                if (!ship.CombatState.RemoveDevice(weapon)) {
                    D.Err("Failed to remove the weapon {0}", weapon);
                }
            }
            isRegistered = add;
            ShipCombatAI.RecalculateBattleStationAssignments(ship.CombatState);
            S.Query.RegisterShipWeapon.Ask(weapon, add);
        }

        private float lastPercentage = -1f;

        private void OnPhaseProgress() {
            var pct = weapon.PhasePercent;
            if (lastPercentage == pct) { return; }
            lastPercentage = pct;
            if (pct > 0f) {
                ShowInfoBar(Consts.ColorChartYellow, pct,
                    $"{T.NounCombatMode}: {weapon.PhaseName} ({Units.Percentage(pct)})");
            } else {
                HideInfoBar();
            }
        }

        private void OnPhaseChange() {
            Tile.ENode.SetConsumerWantedInput(Weapon.CurrentEnergyCost);
            OnPhaseProgress();
            if (weapon.WeaponDef.WeaponType != ShipWeaponType.PointDefense) {
                if (!UpdateStationState()) {
                    return;
                }
            }
            if (weapon.PhaseHasProgress) {
                // we show phase name with % in the bar text
                ShowInfoIcon(weapon.PhaseIcon, null);
            } else {
                ShowInfoIcon(weapon.PhaseIcon, $"{T.NounCombatMode}: {weapon.PhaseName}");
            }
        }

        public bool UpdateStationState() {
            if (weapon.HasNoStation) {
                var numStations = S.Components.CountMatching<BattleStationComp>(
                    bs => bs.WeaponDef == weapon.WeaponDef, 1);
                var expectedWarning = numStations > 0 ? WarningType.NeedMoreStations : WarningType.NoStation;
                if (currentWarning != expectedWarning) {
                    currentWarning = expectedWarning;
                    ShowInfoIcon(IconId.CWarning, NoBattleStationTip(numStations));
                    if (!noStationNotification.IsValid) {
                        noStationNotification = Notification.BuildFor(Tile, null, T.NoBattleStation, 0, NotificationType.Warning)
                            .Group("weap_no_station", icon: IconId.CWarning)
                            .WithTooltipFunc(() => {
                                var numStations = S.Components.CountMatching<BattleStationComp>(
                                    bs => bs.WeaponDef == weapon.WeaponDef, 1);
                                return NoBattleStationTip(numStations);
                            })
                            .Show();
                    }
                }
                return false;
            } else {
                if (currentWarning != WarningType.None) {
                    currentWarning = WarningType.None;
                    HideInfoIcon();
                    Notification.Remove(ref noStationNotification);
                }
            }

            return true;
        }

        private string NoBattleStationTip(int numStations) {
            if (numStations == 0) {
                return T.NoBattleStationTip_V2.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation),
                    Texts.LinkToCodex(Tile.Definition));
            } else {
                return T.NotEnoughBattleStationsTip_V2.F(T.NounBattleStationPlural,
                    Texts.LinkToCodex(Tile.Definition));
            }
        }

        private void OnSetTarget(NPCShip ship, IShipWeaponTarget target) {
            if (target != null) {
                SetTarget(target.TurretTarget);
            } else if (ship != null) {
                SetTarget(ship.View);
            } else {
                SetTarget(null);
            }
        }

        public bool TryShoot(long ticks) {
            if (weapon.Phase == ShipWeaponPhase.Ready) {
                // Ship combat system is handling that
                if (weapon.HasEngageableNonShipTarget) {
                    weapon.Shoot(S, S.Rng);
                }
            }
            return false;
        }
        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.Clock.OnNextUpdate(() => {
                    RegisterWeapon(true);
                });
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    RegisterWeapon(false);
                } else if (Tile.IsConstructed) {
                    RegisterWeapon(true);
                }
            }
        }

        private void TickTurret(long ticks) {
            turret.IsScanning = Tile.ENode.IsConsuming
                && weapon != null
                && Tile.Reinstallable?.IsMidRelocation != true;

            if (ammo != null && weapon != null) {
                if (Tile.ENode.IsPowerable) {
                    ammo.CreateOrUpdateRefillGoal();
                }
                if (ammo.IsEmpty) {
                    turret.IsScanning = false;
                }
            }
        }
        public override void OnRemove() {
            RegisterWeapon(false);
        }

        public void CheckTarget() {
            // nothing to do
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            var config = def.ComponentConfigFor(NameHash);
            if (config == null) { return false; }
            var id = config.GetString(PropertyIdH.ShipWeaponId);
            var device = ShipWeaponDef.Get(id);
            sb.AppendFormat("{0}: <b>{1}</b>", T.NounClassPoints, device.Device.ClassPoints);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
            var config = def.ComponentConfigFor(NameHash);
            if (config == null) { return; }
            var w = ShipWeaponDef.Get(config.GetString(PropertyIdH.ShipWeaponId));
            if (sb.Length > 0) {
                sb.Append("\n\n");
            }
            sb.Append(Texts.SubTitle(T.NounShipWeapon));
            AppendDefInfoLine(sb, T.NounWeaponType, w.WeaponTypeName, false);
            AppendDefInfoLine(sb, T.NounWeaponPower, Units.Num((int) w.CombatStrength));
            AppendDefInfoLine(sb, T.NounDamage,
                $"{Units.Num(w.Damage)} ({DamageTypes.NameFor(w.DamageType)})");
            AppendDefInfoLine(sb, T.StatWindupTime, Units.TicksToTime(w.WindupTimeMinutes, true));
            AppendDefInfoLine(sb, T.StatChargeTime, Units.TicksToTime(w.ChargeTimeMinutes, true));
            AppendDefInfoLine(sb, T.StatFiringTime, Units.TicksToTime(w.FiringTimeMinutes, true));
            if (w.AmmoCapacity > 0) {
                AppendDefInfoLine(sb, T.AmmoCapacity, Units.Num(w.AmmoCapacity));
            }
            AppendFluxDefInfoLine(sb, T.StatFluxPerShotHard, w.HardFluxPerShot);
            AppendFluxDefInfoLine(sb, T.StatFluxPerShotSoft, w.SoftFluxPerShot);
            AppendFluxDefInfoLine(sb, T.StatFluxPerHitHard, w.HardFluxPerShieldHit);
            AppendFluxDefInfoLine(sb, T.StatFluxPerHitSoft, w.SoftFluxPerShieldHit);
        }

        private static void AppendFluxDefInfoLine(StringBuilder sb, string label, int amount) {
            if (amount <= 0) { return; }
            AppendDefInfoLine(sb, label, Units.Num(amount));
        }

        private static void AppendDefInfoLine(StringBuilder sb, string label, string value,
                bool newLine = true) {
            if (newLine) {
                sb.Append('\n');
            }
            sb.Append($"{GlyphMap.ArrowRight} {label}: <b>{value}</b>");
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            ShipWeaponDef.Get(config.GetString(PropertyIdH.ShipWeaponId))
                .Device.AddDefPower(ref power);
        }

        public bool CopyConfigTo(ICopyableComp target) {
            if (target is not ShipWeaponComp other) {
                return false;
            }
            if (weapon == null || other.weapon == null) {
                return false;
            }
            bool changed = false;
            if (weapon.Target != other.weapon.Target) {
                changed = true;
                other.weapon.SetTarget(weapon.Target);
            }
            if (weapon.NonShipTarget != other.weapon.NonShipTarget) {
                changed = true;
                other.SetTarget(weapon.NonShipTarget);
            }
            if (weapon.UseSettings != other.weapon.UseSettings) {
                other.weapon.UseSettings = weapon.UseSettings;
                changed = true;
            }

            if (weapon.Settings != null || other.weapon.Settings != null) {
                if (weapon.Settings == null) {
                    other.weapon.Settings = null;
                    changed = true;
                } else if (!weapon.Settings.Equals(other.weapon.Settings)) {
                    other.weapon.Settings = ShipWeaponSettings.Copy(weapon.Settings);
                    changed = true;
                }
            }
            return changed;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            if (comp is ShipWeaponComp sw) {
                return sw.weapon != null;
            }
            return false;
        }
    }
}
