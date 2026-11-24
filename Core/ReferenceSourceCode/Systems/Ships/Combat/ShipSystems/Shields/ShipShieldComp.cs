using Game.Data;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using Game.Components;
using Game.Components.Upgrades;
using Game.Systems.Ships;
using KL.Collections;
using System;
using Game.Constants;
using Game.Utils;
using System.Collections.Generic;
using System.Text;
using Game.Systems.UIUX;
using Game.Systems.Stats;

namespace Game.Systems.Ships {
    public sealed class ShipShieldComp : BaseComponent<ShipShieldComp>,
            IShipDeviceComp, IDefDescriptionProvider, ISpeedUpgradeable, IStatProvider,
            IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            shownMultiShieldWarning = false;
            AddComponentPrototype(new ShipShieldComp());
        }
        private ShipShield shield;
        public ShipShield Shield => shield;

        public ShipDeviceDef DeviceDef => shieldDef.Device;

        private ShipShieldDef shieldDef;
        private bool isRegistered;
        private uint shipDeviceId;
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

        // Read from the slots rather than counted on install, so it can never drift from
        // what is actually installed (save/load, relocation, ejecting a duplicate)
        public bool HasEMPShield {
            get {
                var slots = Tile.Upgrades?.Upgrades;
                if (slots == null) { return false; }
                for (int i = slots.Length - 1; i >= 0; i--) {
                    if (slots[i] == EMPShieldUpgrade.Id) {
                        return true;
                    }
                }
                return false;
            }
        }

        public string StatsCategoryName => T.NounShipShield;
        public int StatsOrder => 15;

        /// <summary>
        /// Recharge is shortened by Speed upgrades at runtime (see
        /// ShipShield.SetPhase), mirror that here for display.
        /// </summary>
        private string EffTime(int defMinutes) {
            return Units.TicksToTime(Mathf.Max(1, Mathf.RoundToInt(defMinutes / upgradeSpeed)), true);
        }

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (shieldDef == null) { return; }
            var cp = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            cp.Load(IconId.CShield, T.NounClassPoints, Units.Num(shieldDef.Device.ClassPoints));

            var recharge = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            recharge.Load(IconId.CTime, T.StatChargeTime, EffTime(shieldDef.ChargeTimeMinutes));

            // Shield energy consumption depends on ship size, so let's not show it here
            // var energy = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            // energy.Load(IconId.CEnergy, $"{T.EnergyConsumption} ({T.Max})", Units.Electricity(shieldDef.Device.EnergyCostActive));
        }

        private static bool shownMultiShieldWarning;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("ShipDeviceId", shield?.Device.Id ?? 0);
        }

        protected override void OnLoad(ComponentData data) {
            shipDeviceId = data.GetUInt("ShipDeviceId", 0);
        }
        protected override void OnConfig() {
            var id = Config.GetString(PropertyIdH.ShipShieldId);
            shieldDef = ShipShieldDef.Get(id);
            if (shieldDef == null) {
                D.Err("Cannot find ShipShieldDef by id {0} for {1}",
                    id, entity.DefinitionId);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed && !isRegistered) {
                if (wasLoaded) {
                    Ready.When(WhenH.ShipDataRestored, RestoreDevice);
                } else {
                    RegisterShield(true);
                }
            }
            if (!wasLoaded && !shownMultiShieldWarning && !Tile.IsConstructed
                    && !Tile.Reinstallable.IsMidRelocation
                    // A relocation ghost: the existing emitter is the one being moved
                    && Tile.Reinstallable.SourceId == 0
                    && The.Toolbox.ActiveTool == The.Toolbox.BuildTool) {
                var pref = S.Prefs.GetBool(Pref.ShowShieldOverbuildWarning, PrefH.ShowShieldOverbuildWarning, true);
                if (!pref) {
                    shownMultiShieldWarning = true;
                } else {
                    var existingEmitter = S.Components.FindFirstMatching<ShipShieldComp>(sh => sh.IsConstructed && sh.Tile.Health.IsOperational);
                    if (existingEmitter != null) {
                        shownMultiShieldWarning = true;
                        var shieldsTip = T.ShieldEmitterOverbuildTip;
                        var fluxTip = T.BuildFluxCapacitorsTip_V1.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesFluxCapacitor, T.NounFluxCapacitors));
                        var tip = $"{shieldsTip}\n\n{fluxTip}";
                        WPopup.Create(false, IconId.CWarning, T.Warning,
                        T.YouAlreadyHaveObject_V1.F(Texts.LinkToTile(existingEmitter.Tile)), tip)
                            .WithAction(T.Ok, null)
                            .WithAction2(T.DoNotShowAgain, () => {
                                S.Prefs.SetBool(Pref.ShowShieldOverbuildWarning, false);
                            }).Spawn();
                    }
                }
            }
        }
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (Tile.IsConstructed && !isRegistered
                    && EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                RegisterShield(true);
            }
        }

        private void RestoreDevice() {
            if (shipDeviceId != 0) {
                var device = S.Query.FindShipDevice.Ask(
                    S.Universe.Player.Ship, shipDeviceId);
                if (device is ShipShield s) {
                    shield = s;
                    if (shield.Device.Tile != Tile) {
                        D.Err("Shield device tile mismatch! {0}. Tile = {1}, Expected = {2}",
                            shield, Tile, shield.Device.Tile);
                    }
                } else {
                    D.Err("Failed to restore ShipDevice for: {0}. Not a ShipShield: {1}",
                        Tile, device);
                }
            }
            RegisterShield(true, wasLoaded: true);
        }

        public override void OnRemove() {
            RegisterShield(false);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.Clock.OnNextUpdate(() => {
                    RegisterShield(true);
                });
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    RegisterShield(false);
                } else if (Tile.IsConstructed) {
                    RegisterShield(true);
                }
            }
        }

        private void RegisterShield(bool add, bool wasLoaded = false) {
            if (add && !Tile.IsActive) { return; }
            if (add && Tile.Reinstallable.IsMidRelocation) { return; }
            if (add && !EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                D.Warn("Skipping adding shield to tile outside of margin: {0}",
                    Tile);
                return;
            }
            // D.Warn("Registering shield: {0}", add);
            if (isRegistered == add) { return; }
            var ship = S.Universe.Player.Ship;
            if (add) {
                if (shield == null) {
                    // create + add
                    shield = ship.CombatState.AddShield(shieldDef);
                    if (!shield.Device.SetTile(Tile)) {
                        ship.CombatState.RemoveDevice(shield);
                        isRegistered = false;
                        return;
                    }
                    if (wasLoaded) {
                        D.Err("Loaded shield but it was null, recreated: {0}",
                            shield);
                    }
                } else {
                    if (wasLoaded) {
                        if (!ship.CombatState.Shields.Contains(shield)) {
                            ship.CombatState.AddDevice(shield);
                            D.Err("Loaded shield but it wasn't in combat state, adding: {0}",
                                shield);
                        }
                    } else {
                        // readd
                        if (!ship.CombatState.AddDevice(shield)) {
                            D.Err("Trying to add shield twice: {0}", shield);
                        }
                    }
                }
                shield.OnPhaseChange = OnPhaseChange;
                shield.OnPhaseProgressChange = OnPhaseProgress;
                shield.Comp = this;
                OnPhaseChange();
                OnPhaseProgress();
            } else {
                if (!ship.CombatState.RemoveDevice(shield)) {
                    D.Err("Failed to remove the shield {0}", shield);
                }
            }
            isRegistered = add;
            S.Query.RegisterShipShield.Ask(shield, add);
        }
        private float lastPercentage = -1f;
        private void OnPhaseProgress() {
            var pct = shield.PhasePercent;
            if (lastPercentage == pct) { return; }
            lastPercentage = pct;
            if (pct > 0f) {
                ShowInfoBar(Consts.ColorChartYellow, pct,
                    $"{T.NounCombatMode}: {shield.PhaseName} ({Units.Percentage(pct)})");
            } else {
                HideInfoBar();
            }
        }

        private void OnPhaseChange() {
            OnPhaseProgress();
            Tile.ENode.SetConsumerWantedInput(Shield.CurrentEnergyCost);
            if (shield.PhaseHasProgress) {
                // we show phase name with % in the bar text
                ShowInfoIcon(shield.PhaseIcon, null);
            } else {
                ShowInfoIcon(shield.PhaseIcon, $"{T.NounCombatMode}: {shield.PhaseName}");
            }
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            var config = def.ComponentConfigFor(NameHash);
            if (config == null) { return false; }
            var id = config.GetString(PropertyIdH.ShipShieldId);
            var device = ShipShieldDef.Get(id);
            sb.AppendFormat("{0}: <b>{1}</b>", T.NounClassPoints, device.Device.ClassPoints);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        // The active cost grows with ship size, over the whole size curve
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var device = ShipShieldDef.Get(config.GetString(PropertyIdH.ShipShieldId)).Device;
            Curve sizeMulCurve = Tunables.ShipSizeShieldEnergyCostCurve;
            var points = sizeMulCurve.Points;
            var mulMin = float.MaxValue;
            var mulMax = 0f;
            foreach (var p in points) {
                mulMin = Mathf.Min(mulMin, p.y);
                mulMax = Mathf.Max(mulMax, p.y);
            }
            if (mulMax <= 0f) {
                mulMin = mulMax = 1f;
            }
            device.AddDefPower(ref power, mulMin, mulMax);
        }

        public void UpdateEnergyCost() {
            if (shield != null) {
                Tile.ENode.SetConsumerWantedInput(
                    Shield.CurrentEnergyCost);
            }
        }
    }
}
