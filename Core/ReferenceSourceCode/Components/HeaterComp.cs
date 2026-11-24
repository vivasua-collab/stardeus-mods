using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using Game.Components.Upgrades;
using UnityEngine;

namespace Game.Components {
    public sealed class HeaterComp : BaseComponent<HeaterComp>,
            ICopyableComp, IRelocatable,
            IEnergyEfficiencyUpgradeable, IRangedAreaOutline,
            IDefDescriptionProvider, IDefPowerProvider {
        private const float DefaultEnergyCost = 0f;

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new HeaterComp());
        }
        private float range;
        private float rate;
        private float limit;
        private float defaultLimit;
        private float energyCost;
        // Heat (or cold) keeps spreading from the pinned tiles within range, so
        // the area that actually changes temperature reaches further. 0 shows no range
        private float displayRange;
        public float Range => displayRange;
        public Color RadiusColor => isCooler ? Consts.ColorCoolerRange : Consts.ColorHeaterRange;
        public bool RadiusSameArea => true;

        private readonly List<int> surroundings = new();
        private bool grabbedSurroundings;

        private const float ThermostatBandKelvin = 1f;
        private bool isOperating = true;

        private int centerIdx;
        private bool isCooler;
        private bool isDisabled;
        public bool IsCooler => isCooler;
        public bool IsDisabled => isDisabled;
        public float DefaultLimit => defaultLimit;
        public float Limit {
            get => limit;
            set => limit = value;
        }
        public void ResetLimit() {
            limit = defaultLimit;
        }
        public void SetDisabled(bool disabled) {
            isDisabled = disabled;
            if (isDisabled && elNode != null) {
                A.Grid.RemoveAfterTick(this, AfterTickGrid);
                elNode = null;
            }
        }

        public override void UpdatePosIdx(int pos, int dx, int dy) {
            centerIdx = Pos.NeighborAt(centerIdx, dx, dy);
            grabbedSurroundings = false;
            surroundings.Clear();
        }

        public override void OnSave() {
            var data = GetOrCreateData();
            data.SetFloat("Limit", limit);
            data.SetBool("IsDisabled", isDisabled);
        }

        protected override void OnLoad(ComponentData data) {
            limit = data.GetFloat("Limit", limit);
            isDisabled = data.GetBool("IsDisabled", isDisabled);
        }

        protected override void OnConfig() {
            isCooler = Config.GetBool(PropertyIdH.IsCooler, false);
            rate = Config.GetFloat(PropertyIdH.Rate);
            range = Config.GetInt(PropertyIdH.Range, 5);
            displayRange = Config.GetInt(PropertyIdH.DisplayRange, 0);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            limit = Units.CelsiusToKelvin(Config.GetFloat(PropertyIdH.LimitCelsius, 21));
            defaultLimit = limit;
            // Read only by the codex (DefEffectiveness)
            Config.MarkUsed(PropertyIdH.Effectiveness);
        }
        public override void OnLateReady(bool wasLoaded) {
            centerIdx = Tile.CenterIdx;
        }

        public override string ToString() {
            return string.Format(
                " * Heater [Rate: {0}, Lim: {1}C, Range: {2}]",
                    rate, Units.KelvinToCelsius(limit), range);
        }
        private ElectricNodeComp elNode;
        private float upgradeEnergyEfficiency = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;

        private float EffectiveEnergyCost {
            get {
                var cost = energyCost;
                if (upgradeEnergyEfficiency > 1f) {
                    cost *= 1f / upgradeEnergyEfficiency;
                }
                return cost;
            }
        }

        private void SetOperating(bool operating) {
            if (isOperating == operating) { return; }
            isOperating = operating;
            if (elNode != null) {
                elNode.SetConsumerWantedInput(operating ? EffectiveEnergyCost : 0f);
            }
        }

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
            // Apply the new efficiency immediately if the electric node is available
            if (elNode != null) {
                elNode.SetConsumerWantedInput(isOperating ? EffectiveEnergyCost : 0f);
            }
        }

        public bool IsCopyable => true;

        public string CopyConfigText => isCooler ? T.Cooler : T.Heater;

        public override void Receive(IComponent sender, int message) {
            if (isDisabled) { return; }
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.SetConsumerWantedInput(isOperating ? EffectiveEnergyCost : 0f);
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        // This will always run on tickmod == 0 due to energy grid ticking
        // We read back heat and oxygen in EquilibriumSys at tickMod 5 & 6
        private void AfterTickGrid() {
            if (isDisabled || !Ready.Areas || !Ready.AtmoTemp) { return; }

            // Thermostat: read our own tile and decide whether to operate.
            var ownTemp = S.GetTemp(centerIdx);
            bool shouldOperate = isCooler
                ? ownTemp > limit - ThermostatBandKelvin   // cooler idles once cold enough
                : ownTemp < limit + ThermostatBandKelvin;  // heater idles once warm enough
            SetOperating(shouldOperate);

            if (!shouldOperate || !elNode.IsConsuming) { return; }

            var area = S.Sys.Areas;
            var curArea = area.At(centerIdx);

            // Heating
            if (!grabbedSurroundings) {
                grabbedSurroundings = S.Grid.GetSurroundings(
                    centerIdx, range, surroundings);
            }

            // TODO cache active surroundings area checks
            //var ins = S.Insulation.Data;
            if (isCooler) {
                for (int i = surroundings.Count - 1; i >= 0; i--) {
                    int pos = surroundings[i];
                    // not heat something outside the area
                    if (area.At(pos) != curArea) { continue; }
                    var value = S.GetTemp(pos);
                    // Otherwise we kill the fire heat
                    if (value <= limit) { continue; }
                    value -= rate;
                    var target = value < limit ? limit : value;
                    S.SetTemp(pos, target);
                    // Hold the tile there while the step's iterations mix it
                    S.PinTempAtMost(pos, target);
                }
            } else {
                for (int i = surroundings.Count - 1; i >= 0; i--) {
                    int pos = surroundings[i];
                    // not heat something outside the area
                    if (area.At(pos) != curArea) { continue; }
                    // Floor has no insulation
                    if (S.GetTempResistance(pos) < 0.045f) { continue; }
                    var value = S.GetTemp(pos);
                    // Otherwise we kill the fire heat
                    if (value >= limit) { continue; }
                    value += rate;
                    var target = value > limit ? limit : value;
                    S.SetTemp(pos, target);
                    // Hold the tile there while the step's iterations spread it
                    S.PinTempAtLeast(pos, target);
                }
            }
        }

        public bool CopyConfigTo(ICopyableComp target) {
            bool changed = false;
            if (target is HeaterComp heater) {
                if (heater.limit != limit) {
                    heater.limit = limit;
                    changed = true;
                }
            }
            return changed;
        }

        public bool CanCopyTo(ICopyableComp comp) {
            if (comp is HeaterComp heater) {
                return heater.isCooler == isCooler;
            }
            return false;
        }

        public void RelocateTo(Entity target) {
            if (target.TryGetComponent<HeaterComp>(out var heater)) {
                CopyConfigTo(heater);
            }
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            return DefEffectiveness.AppendTo(sb, def, def.ComponentConfigFor(NameHash, false),
                addedAnything);
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }
    }
}
