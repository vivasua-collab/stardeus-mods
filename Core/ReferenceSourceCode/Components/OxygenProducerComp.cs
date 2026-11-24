using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using Game.Components.Upgrades;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Game.Components {
    public sealed class OxygenProducerComp : BaseComponent<OxygenProducerComp>,
            IEnergyEfficiencyUpgradeable, IRangedAreaOutline,
            IDefDescriptionProvider, IDefPowerProvider {
        private const float DefaultEnergyCost = 0f;

        public ElectricNodeComp ENode => elNode;
        private bool grabbedSurroundings;
        private readonly List<int> surroundings = new();

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new OxygenProducerComp());
        }
        public bool IsDisabled => isDisabled;
        public float Limit => limit;
        public float DefaultLimit => defaultLimit;
        public void SetLimit(float v) { limit = v; }
        public void ResetLimit() { limit = defaultLimit; }

        private float range;
        // Oxygen keeps spreading from the pinned tiles within range, so the
        // area that actually gets breathable reaches further. 0 shows no range
        private float displayRange;
        public float Range => displayRange;
        public Color RadiusColor => Consts.ColorOxygenRange;
        public bool RadiusSameArea => true;
        private float rate;
        private float limit;
        private float defaultLimit;
        public RefillableStorageComp Fuel;
        private float fuelConsumptionPerDay;
        private float energyCost;
        private int centerIdx;
        private bool isDisabled;
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

        protected override void OnConfig() {
            fuelConsumptionPerDay = Config.GetFloat(PropertyIdH.FuelPerDay, 1f);
            rate = Config.GetFloat(PropertyIdH.Rate);
            range = Config.GetInt(PropertyIdH.Range, 2);
            displayRange = Config.GetInt(PropertyIdH.DisplayRange, 0);
            limit = Config.GetFloat(PropertyIdH.Max,
                Consts.MaxAtmosphereOxygenSaturation);
            defaultLimit = limit;
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            // Read only by the codex (DefEffectiveness)
            Config.MarkUsed(PropertyIdH.Effectiveness);
        }

        public override string ToString() {
            return string.Format(
                " * OxygenProducer [Rate: {0}, Lim: {1}C, Range: {2}]",
                    rate, limit, range);
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

        public override void OnLateReady(bool wasLoaded) {
            Entity.TryGetComponent(out Fuel);
            centerIdx = Tile.CenterIdx;
        }
        private ElectricNodeComp elNode;
        private float upgradeEnergyEfficiency = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
            // Apply the new efficiency immediately if the electric node is available
            if (elNode != null && Fuel == null) {
                // For constant burn mode, update immediately
                var actualEnergyCost = energyCost;
                if (upgradeEnergyEfficiency > 1f) {
                    actualEnergyCost *= 1f / upgradeEnergyEfficiency;
                }
                elNode.SetConsumerWantedInput(actualEnergyCost);
            }
            // For fuel-based mode, it will be updated in the next AfterTickGrid
        }
        public override void Receive(IComponent sender, int message) {
            if (isDisabled) { return; }
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    if (Fuel == null) {
                        // constant burn
                        var actualEnergyCost = energyCost;
                        if (upgradeEnergyEfficiency > 1f) {
                            actualEnergyCost *= 1f / upgradeEnergyEfficiency;
                        }
                        elnode.SetConsumerWantedInput(actualEnergyCost);
                    }
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }

        private void AfterTickGrid() {
            if (isDisabled || !Ready.Areas || !Ready.AtmoO2) { return; }
            if (!elNode.IsPowerable || elNode.HasDeficit) { return; }

            if (Fuel != null) {
                if (Fuel.IsEmpty) {
                    Fuel.NotifyLack();
                    Fuel.CreateOrUpdateRefillGoal();
                    elNode.SetConsumerWantedInput(0f);
                    return;
                }
            }

            var area = S.Sys.Areas;
            var curArea = area.At(centerIdx);

            // Only oxygenate if in a tight room! NOT
            //if (curArea == 0) { return energyCost; }


            // Oxygenating
            if (!grabbedSurroundings) {
                grabbedSurroundings = S.Grid.GetSurroundings(
                    centerIdx, range, surroundings);
            }
            bool didRun = false;
            //var air = S.Airtight.Data;
            for (int i = surroundings.Count - 1; i >= 0; i--) {
                int pos = surroundings[i];
                // not heat something outside the area
                if (area.At(pos) != curArea) { continue; }
                // Don't oxygenate non-airtight tiles
                var level = S.IncrO2(pos, rate, limit, 0.045f);
                // Hold the tile at what it reached while the step's iterations spread
                // it, capped at the limit so a richer tile is not propped up by us
                S.PinO2AtLeast(pos, level < limit ? level : limit);
                didRun = true;
            }

            if (didRun && Fuel != null) {
                var ratePerTick = fuelConsumptionPerDay / Consts.TicksPerDay;
                // because we run electricity every 10 ticks
                ratePerTick *= 10;
                if (Fuel.TryConsume(ratePerTick)) {
                    var actualEnergyCost = energyCost;
                    if (upgradeEnergyEfficiency > 1f) {
                        actualEnergyCost *= 1f / upgradeEnergyEfficiency;
                    }
                    elNode.SetConsumerWantedInput(actualEnergyCost);
                    Fuel.ClearDeficitFlag();
                    Fuel.CreateOrUpdateRefillGoal();
                } else {
                    // no fuel = no oxygen
                    elNode.SetConsumerWantedInput(0f);
                    return;
                }
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
