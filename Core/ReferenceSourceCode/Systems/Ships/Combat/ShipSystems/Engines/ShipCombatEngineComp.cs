using Game.Data;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using Game.Components;
using Game.Systems.Ships;
using KL.Collections;
using Game.Constants;
using Game.Utils;
using System.Collections.Generic;
using System.Text;
using System;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Systems.Ships {
    public sealed class ShipCombatEngineComp : BaseComponent<ShipCombatEngineComp>,
            IShipDeviceComp, IDefDescriptionProvider, IStatProvider, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ShipCombatEngineComp());
        }
        private ShipEngine engine;
        public float EvadeBonus;
        private uint shipDeviceId;
        private ShipEngineDef engineDef;
        private bool isRegistered;
        private float lastPercentage = -1f;
        public ShipDeviceDef DeviceDef => engineDef.Device;

        protected override void OnConfig() {
            var id = Config.GetString(PropertyIdH.ShipEngineId);
            engineDef = ShipEngineDef.Get(id);
            if (engineDef == null) {
                D.Err("Cannot find ShipEngineDef by id {0} for {1}",
                    id, entity.DefinitionId);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            if (Tile.IsConstructed && !isRegistered) {
                if (wasLoaded) {
                    Ready.When(WhenH.ShipDataRestored, RestoreDevice);
                } else {
                    RegisterEngine(true);
                }
            }
        }
        public override void UpdatePosIdx(int pos, int dx, int dy) {
            if (Tile.IsConstructed && !isRegistered
                    && EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                RegisterEngine(true);
            }
        }
        private void RestoreDevice() {
            if (shipDeviceId != 0) {
                var device = S.Query.FindShipDevice.Ask(
                    S.Universe.Player.Ship, shipDeviceId);
                if (device is ShipEngine e) {
                    engine = e;
                    if (engine.Device.Tile != Tile) {
                        D.Err("Engine device tile mismatch! {0}. Tile = {1}, Expected = {2}",
                            engine, Tile, engine.Device.Tile);
                    }
                } else {
                    D.Err("Failed to restore ShipDevice for: {0}. Not a ShipEngine: {1}",
                        Tile, device);
                }
            }
            RegisterEngine(true, wasLoaded: true);
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetUInt("ShipDeviceId", engine?.Device.Id ?? 0);
        }

        protected override void OnLoad(ComponentData data) {
            shipDeviceId = data.GetUInt("ShipDeviceId", 0);
        }

        public override void OnRemove() {
            RegisterEngine(false);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ConstructionFinished) {
                S.Clock.OnNextUpdate(() => {
                    RegisterEngine(true);
                });
            } else if (message == MsgIdH.RelocationStateChanged) {
                if (Tile.Reinstallable.IsMidRelocation) {
                    RegisterEngine(false);
                } else if (Tile.IsConstructed) {
                    RegisterEngine(true);
                }
            }
        }

        private void RegisterEngine(bool add, bool wasLoaded = false) {
            if (add && !Tile.IsActive) { return; }
            if (add && Tile.Reinstallable.IsMidRelocation) { return; }
            if (add && !EntityUtils.IsWithinMargin(Tile.CenterIdx, false, false)) {
                D.Warn("Skipping adding engine to tile outside of margin: {0}",
                    Tile);
                return;
            }
            // D.Warn("Registering engine: {0} | {1}", add, Tile);
            if (isRegistered == add) { return; }
            var ship = S.Universe.Player.Ship;
            if (add) {
                if (engine == null) {
                    // create + add
                    engine = ship.CombatState.AddEngine(engineDef);
                    if (!engine.Device.SetTile(Tile)) {
                        ship.CombatState.RemoveDevice(engine);
                        isRegistered = false;
                        return;
                    }
                    if (wasLoaded) {
                        // normal when loading old saves
                        if (!S.Vars.IsLoadingLegacySave) {
                            D.Err("Loaded engine but it was null, recreated: {0}",
                                engine);
                        }
                    }
                } else {
                    if (wasLoaded) {
                        if (!ship.CombatState.Engines.Contains(engine)) {
                            ship.CombatState.AddDevice(engine);
                            D.Err("Loaded engine but it wasn't in combat state, adding: {0}",
                                engine);
                        }
                    } else {
                        // readd
                        if (!ship.CombatState.AddDevice(engine)) {
                            D.Err("Trying to add engine twice: {0}", engine);
                        }
                    }
                }
                engine.OnPhaseChange = OnPhaseChange;
                engine.OnPhaseProgressChange = OnPhaseProgress;
                engine.Comp = this;
                OnPhaseChange();
                OnPhaseProgress();
            } else {
                if (!ship.CombatState.RemoveDevice(engine)) {
                    D.Err("Failed to remove the engine {0}", engine);
                }
            }
            isRegistered = add;
            S.Query.RegisterShipEngine.Ask(engine, add);
        }

        private void OnPhaseProgress() {
            if (!A.Player.Ship.IsInCombat) { return; }
            var pct = engine.PhasePercent;
            if (lastPercentage == pct) { return; }
            lastPercentage = pct;
            if (pct > 0f) {
                ShowInfoBar(Consts.ColorChartYellow, pct,
                    $"{T.NounCombatMode}: {engine.PhaseName} ({Units.Percentage(pct)})");
            } else {
                HideInfoBar();
            }
        }

        private void OnPhaseChange() {
            if (A.Player.Ship.IsInCombat) {
                Tile.ENode.SetConsumerWantedInput(engine.CurrentEnergyCost);
                OnPhaseProgress();
                if (engine.PhaseHasProgress) {
                    // we show phase name with % in the bar text
                    ShowInfoIcon(engine.PhaseIcon, null);
                } else {
                    ShowInfoIcon(engine.PhaseIcon, $"{T.NounCombatMode}: {engine.PhaseName}");
                }
            } else {
                if (!A.IsInHyperspace) {
                    Tile.ENode.SetConsumerWantedInput(0f);
                }
                HideInfoIcon();
                HideInfoBar();
            }
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            Def.PrependAdditionalInfo(sb, addedAnything);
            var config = def.ComponentConfigFor(NameHash);
            if (config == null) { return false; }
            var id = config.GetString(PropertyIdH.ShipEngineId);
            var device = ShipEngineDef.Get(id);
            sb.AppendFormat("{0}: <b>{1}</b>", T.NounClassPoints, device.Device.ClassPoints);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            ShipEngineDef.Get(config.GetString(PropertyIdH.ShipEngineId))
                .Device.AddDefPower(ref power);
        }

        public void AddEvadeBonus(float v) {
            EvadeBonus += v;
        }

        public string StatsCategoryName => T.NounShipEngine;
        public int StatsOrder => 15;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            if (engineDef == null) { return; }
            var cp = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
            cp.Load(IconId.CPropulsion, T.NounClassPoints, Units.Num(engineDef.Device.ClassPoints));

            if (engineDef.Evade > 0) {
                var evade = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                evade.Load(IconId.CPropulsion, T.NounEvade, Units.Num(engineDef.Evade));

                var charge = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                charge.Load(IconId.CTime, T.StatChargeTime, Units.TicksToTime(engineDef.ChargeTimeMinutes, true));

                var energy = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContentText2, block.Container, idx++);
                energy.Load(IconId.CEnergy, $"{T.EnergyConsumption} ({T.Max})", Units.Electricity(engineDef.Device.EnergyCostActive));
            }
        }
    }
}
