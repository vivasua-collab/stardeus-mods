using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Components.Upgrades;

namespace Game.Components {
    public sealed class SolarPanelComp : BaseComponent<SolarPanelComp>,
            ITickListener, IEnergyEfficiencyUpgradeable, IDefPowerProvider {

        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new SolarPanelComp());
        }
        private const float DefaultPowerOutput = 10f;

        // Rated output of a clean panel in normal space. Dust lowers it, stars and
        // photon storms raise it
        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            var output = config.GetFloat(PropertyIdH.PowerOutput, DefaultPowerOutput);
            power.AddProduction(output, output);
        }

        private static Curve solarPanelOutput;
        private CleanableComp Cleanable;

        private float powerOutput;
        private float lightOnOutputLevel;
        private float dustAccSpeed;
        private float currentOutput;

        public override void OnLateReady(bool wasLoaded) {
            Cleanable = Entity.GetComponent<CleanableComp>();
            if (wasLoaded) {
                UpdateCurrentOutput(false);
            }
        }

        protected override void OnConfig() {
            powerOutput = Config.GetFloat(PropertyIdH.PowerOutput, DefaultPowerOutput);
            lightOnOutputLevel = powerOutput * 0.5f;
            dustAccSpeed = Config.GetFloat(PropertyIdH.DustAccumulation, 0.001f);
            currentOutput = powerOutput;
        }

        public override string ToString() {
            return $" * SolarPanel [Power {currentOutput}/{powerOutput}]";
        }

        private ElectricNodeComp enode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    this.enode = enode;
                    UpdateCurrentOutput(false);
                }
            }
        }

        public bool IsEnergyEfficiencyUpgradeable => Tile.IsConstructed;

        public void Tick(long currentTick) {
            if (!Tile.IsConstructed) { return; }
            if (currentTick % 10 == EntityId % 10) {
                UpdateCurrentOutput(true);
            }
        }

        private void UpdateCurrentOutput(bool addDust) {
            if (Cleanable == null) { return; }
            var solarMul = S.BB.Get(BBKeyH.SolarOutputMultiplier, 1f);
            currentOutput = EvaluateSolarOutput(powerOutput
                * upgradeEnergyEfficiency, Cleanable.DirtPercent)
                    * Tile.Health.HealthPercent * solarMul;
            if (currentOutput < lightOnOutputLevel && Tile.Light.IsEmitting) {
                Tile.Light.SwitchLight(false);
            } else if (currentOutput > lightOnOutputLevel && !Tile.Light.IsEmitting) {
                Tile.Light.SwitchLight(true);
            }
            enode?.SetProducerMaxOutput(currentOutput);
            if (addDust) {
                var dustMul = S.BB.Get(BBKeyH.DustMultiplier, 1f);
                var addDustAmt = dustAccSpeed * dustMul * S.Rng.Range(0.9f, 1.1f);
                Cleanable.AddDirt(addDustAmt);
            }
        }

        private float upgradeEnergyEfficiency = 1f;
        public float UpgradeEnergyEfficiency => upgradeEnergyEfficiency;
        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} ->{1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
            UpdateCurrentOutput(false);
        }

        public static float EvaluateSolarOutput(float maxOutput, float dirtAmount) {
            solarPanelOutput ??= CurveDef.FastGet(CurveIdH.Electricity_SolarPanelDustOutput);
            return solarPanelOutput.Evaluate(dirtAmount) * maxOutput;
        }

    }
}
