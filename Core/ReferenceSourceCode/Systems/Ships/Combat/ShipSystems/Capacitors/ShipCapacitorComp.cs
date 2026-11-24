using Game.Data;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using Game.Components;
using Game.Systems.Ships;
using KL.Collections;
using System;
using Game.Constants;
using Game.Utils;
using System.Collections.Generic;
using System.Text;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Systems.Ships {
    public sealed class ShipCapacitorComp : BaseComponent<ShipCapacitorComp>,
            IShipDeviceComp, IDefDescriptionProvider, IStatProvider, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipCapacitorComp());
        }

        private ShipCapacitor capacitor;
        public ShipCapacitor Capacitor => capacitor;

        public ShipDeviceDef DeviceDef => capacitorDef.Device;
        public int FluxCapacity => capacitorDef?.Capacity ?? 0;

        private ShipCapacitorDef capacitorDef;
        private bool isRegistered;
        private uint shipDeviceId;

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("ShipDeviceId", capacitor?.Device.Id ?? 0);
        }

        protected override void OnLoad(ComponentData data) {
            shipDeviceId = data.GetUInt("ShipDeviceId", 0);
        }

        protected override void OnConfig() {
            var id = Config.GetString(PropertyIdH.ShipCapacitorId);
            capacitorDef = ShipCapacitorDef.Get(id);
            if (capacitorDef == null) {
                D.Err("Cannot find ShipCapacitorDef by id {0} for {1}",
                    id, entity.DefinitionId);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed && !isRegistered) {
                if (wasLoaded) {
                    Ready.When(WhenH.ShipDataRestored, RestoreDevice);
                } else {
                    RegisterCapacitor(true);
                }
            }
        }
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (Tile.IsConstructed && !isRegistered
                    && EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                RegisterCapacitor(true);
            }
        }

        private void RestoreDevice() {
            if (shipDeviceId != 0) {
                var device = S.Query.FindShipDevice.Ask(
                    S.Universe.Player.Ship, shipDeviceId);
                if (device is ShipCapacitor c) {
                    capacitor = c;
                    if (capacitor.Device.Tile != Tile) {
                        D.Err("Capacitor device tile mismatch! {0}. Tile = {1}, Expected = {2}",
                            capacitor, Tile, capacitor.Device.Tile);
                    }
                } else {
                    D.Err("Failed to restore ShipDevice for: {0}. Not a ShipCapacitor: {1}",
                        Tile, device);
                }
            }
            RegisterCapacitor(true, wasLoaded: true);
        }

        public override void OnRemove() {
            RegisterCapacitor(false);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.Clock.OnNextUpdate(() => {
                    RegisterCapacitor(true);
                });
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    RegisterCapacitor(false);
                } else if (Tile.IsConstructed) {
                    RegisterCapacitor(true);
                }
            }
        }

        private float lastPercentage = -1f;
        private void OnPhaseProgress() {
            var pct = capacitor.PhasePercent;
            if (lastPercentage == pct) { return; }
            lastPercentage = pct;
            if (pct > 0f) {
                ShowInfoBar(Consts.ColorChartYellow, pct,
                    $"{T.NounCombatMode}: {capacitor.PhaseName} ({Units.Percentage(pct)})");
            } else {
                HideInfoBar();
            }
        }

        private void OnPhaseChange() {
            OnPhaseProgress();
            Tile.ENode.SetConsumerWantedInput(capacitor.CurrentEnergyCost);
            if (capacitor.PhaseHasProgress) {
                ShowInfoIcon(capacitor.PhaseIcon, null);
            } else {
                ShowInfoIcon(capacitor.PhaseIcon, $"{T.NounCombatMode}: {capacitor.PhaseName}");
            }
        }

        private void RegisterCapacitor(bool add, bool wasLoaded = false) {
            if (add && !Tile.IsActive) { return; }
            if (add && Tile.Reinstallable.IsMidRelocation) { return; }
            if (add && !EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                D.Warn("Skipping adding capacitor to tile outside of margin: {0}",
                    Tile);
                return;
            }
            if (isRegistered == add) { return; }
            var ship = S.Universe.Player.Ship;
            if (add) {
                if (capacitor == null) {
                    capacitor = ship.CombatState.AddCapacitor(capacitorDef);
                    if (!capacitor.Device.SetTile(Tile)) {
                        ship.CombatState.RemoveDevice(capacitor);
                        isRegistered = false;
                        return;
                    }
                    if (wasLoaded) {
                        D.Err("Loaded capacitor but it was null, recreated: {0}",
                            capacitor);
                    }
                } else {
                    if (wasLoaded) {
                        if (!ship.CombatState.Capacitors.Contains(capacitor)) {
                            ship.CombatState.AddDevice(capacitor);
                            D.Err("Loaded capacitor but it wasn't in combat state, adding: {0}",
                                capacitor);
                        }
                    } else {
                        if (!ship.CombatState.AddDevice(capacitor)) {
                            D.Err("Trying to add capacitor twice: {0}", capacitor);
                        }
                    }
                }
                capacitor.OnPhaseChange = OnPhaseChange;
                capacitor.OnPhaseProgressChange = OnPhaseProgress;
                capacitor.Comp = this;
                OnPhaseChange();
                OnPhaseProgress();
            } else {
                if (!ship.CombatState.RemoveDevice(capacitor)) {
                    D.Err("Failed to remove the capacitor {0}", capacitor);
                }
            }
            isRegistered = add;
            S.Query.RegisterShipCapacitor.Ask(capacitor, add);
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            var config = def.ComponentConfigFor(NameHash);
            if (config == null) { return false; }
            var id = config.GetString(PropertyIdH.ShipCapacitorId);
            var device = ShipCapacitorDef.Get(id);
            sb.AppendFormat("{0}: <b>{1}</b>", T.NounFluxBonus, device.Capacity);
            sb.AppendFormat(", {0}: <b>{1}</b>", T.NounClassPoints, device.Device.ClassPoints);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            ShipCapacitorDef.Get(config.GetString(PropertyIdH.ShipCapacitorId))
                .Device.AddDefPower(ref power);
        }

        public string StatsCategoryName => T.NounFluxCapacitor;
        public int StatsOrder => 15;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (capacitorDef == null) { return; }
            var cp = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            cp.Load(IconId.CFlux, T.NounClassPoints, Units.Num(capacitorDef.Device.ClassPoints));

            var bonus = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            bonus.Load(IconId.CFlux, T.NounFluxBonus, Units.Num(capacitorDef.Capacity));

            var charge = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            charge.Load(IconId.CTime, T.StatChargeTime, Units.TicksToTime(capacitorDef.ChargeTimeMinutes, true));

            var energy = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            energy.Load(IconId.CEnergy, $"{T.EnergyConsumption} ({T.Max})", Units.Electricity(capacitorDef.Device.EnergyCostActive));
        }
    }
}
