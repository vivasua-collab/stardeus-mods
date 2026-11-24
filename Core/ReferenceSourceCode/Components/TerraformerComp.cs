using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Game.Components.Upgrades;

namespace Game.Components {
    public sealed class TerraformerComp : BaseComponent<TerraformerComp>,
            IEnergyEfficiencyUpgradeable, ISpeedUpgradeable, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TerraformerComp());
        }
        private const float DefaultEnergyCost = 500f;
        private float energyCost;
        private float progress;
        private TerraformType Type;
        public RefillableStorageComp Material;

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            var type = Config.GetString(PropertyIdH.Type, null);
            Type = TerraformType.Get(type);
            matConsumptionPerGridTick = Type.MaterialConsumptionPerDay / Consts.TicksPerDay * 10f;
        }
        public override string ToString() {
            return $" * Terraformer [{Type}]";
        }

        public override void OnRemove() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig.TerraformUpdated.RemoveListener(OnTerraformUpdated);
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            Material = Entity.GetComponent<RefillableStorageComp>();
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.TerraformUpdated.AddListener(OnTerraformUpdated);
            }
        }

        private void OnTerraformUpdated(TerraformType type, float p) {
            if (Type != type) { return; }
            progress = p;
        }

        private bool isSubscribed;
        private float matConsumptionPerGridTick;


        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;
        private float upgradeEnergyEfficiency = 1f;
        private float upgradeSpeed = 1f;

        public bool IsEnergyEfficiencyUpgradeable => true;
        public bool IsSpeedUpgradeable => true;

        public void ApplyEfficiencyUpgrade(float amt) {
            upgradeEnergyEfficiency += amt;
            if (upgradeEnergyEfficiency < 1f) {
                D.Err("Upgrade Efficiency < 1f after apply: {0} -> {1}",
                    amt, upgradeEnergyEfficiency);
                upgradeEnergyEfficiency = 1f;
            }
        }

        public void ApplySpeedUpgrade(float amt) {
            upgradeSpeed += amt;
            if (upgradeSpeed < 1f) {
                D.Err("Upgrade speed < 1f after apply: {0} -> {1}",
                    amt, upgradeSpeed);
                upgradeSpeed = 1f;
            }
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                }
            }
        }
        private void AfterTickGrid() {
#if !STARDEUS_DEMO
            // Terraforming completed
            if (progress >= 1f) {
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            if (!elNode.IsPowerable) { return; }
            var lastPing = S.BB.Get(BBKeyH.TerraformControlsOperatorPing, (long) -Consts.TicksPerDay);
            if (G.Ticks - lastPing > Consts.TicksPerHour) {
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            if (Material.IsEmpty) {
                Material.NotifyLack();
                Material.CreateOrUpdateRefillGoal();
                elNode.SetConsumerWantedInput(0f);
                return;
            }
            var actualEnergyCost = energyCost;
            if (upgradeEnergyEfficiency > 1f) {
                actualEnergyCost *= 1f / upgradeEnergyEfficiency;
            }
            elNode.SetConsumerWantedInput(actualEnergyCost);
            if (!elNode.IsConsuming) { return; }
            if (Material.TryConsume(matConsumptionPerGridTick)) {
                var timeDays = Type.DurationDays / Tunables.TerraformSpeed;
                var prog = 10f / Consts.TicksPerDay / timeDays;
                if (upgradeSpeed > 1f) {
                    prog *= upgradeSpeed;
                }
                if (prog > 0) {
                    if (prog > 1f) {
                        D.Err("Trying to do > 1 terraforming progress! Tick passed: {0}. Time Days: {1}",
                            10, timeDays);
                    } else {
                        S.Sig.TerraformProgress.Send(Type, prog);
                    }
                }
                Material.CreateOrUpdateRefillGoal();
            }
#endif
        }
    }
}
