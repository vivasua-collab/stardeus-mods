using Game.Achievements;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Unity.Mathematics;

namespace Game.Components {
    public sealed class TelescopeComp : BaseComponent<TelescopeComp>, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new TelescopeComp());
        }

        private const float DefaultEnergyCost = 250f;
        private float energyCost;
        private float lastPercentage;

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * Telescope";
        }

        public override void OnLateReady(bool wasLoaded) {
            UpdateInfoBar();
        }

        public float MaxConsumption => energyCost;

        private void UpdateInfoBar() {
            if (!Tile.IsConstructed) { return; }
            if (S.TelescopeScanCount > 0) {
                HideInfoBar();
                return;
            }
            var percentage = S.TelescopeProgress;
            if (lastPercentage == percentage) { return; }
            lastPercentage = percentage;
            var bar = ShowInfoBar(Consts.GreenRectColor, percentage,
                percentage * 100, 100,
                    T.PercentageScanBar_V1.F(Units.Percentage(percentage)));
            bar.IsPretranslated = true;
        }
        private ElectricNodeComp elNode;
        public ElectricNodeComp ENode => elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    elnode.SetConsumerWantedInput(energyCost);
                }
            }
        }

        private void AfterTickGrid() {
            if (elNode.IsConsuming) {
                if (Tile.Outdoors.IsOutdoors) {
                    AdvanceProgress();
                    UpdateInfoBar();
                }
            }
        }

        private void AdvanceProgress() {
            if (S.TelescopeScanCount > 0) { return; }
            var timeDays = Tunables.TelescopeScanTimeDays;
            var prog = 10f / Consts.TicksPerDay
                // make it more difficult to scan the 2nd and following planets
                / math.pow(timeDays, S.TelescopeScanCount + 1);
            S.TelescopeProgress += prog;
            if (S.TelescopeProgress >= 1f) {
                S.TelescopeProgress = 0;
                S.TelescopeScanCount++;
                S.Sig.TelescopeScanComplete.Send();
                The.AchievementTracker.Unlock(
                    AchievementDiscoverTerraformable.AchievementId);
                UpdateInfoBar();
            }
        }
    }
}
