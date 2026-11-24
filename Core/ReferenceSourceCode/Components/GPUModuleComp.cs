using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class GPUModuleComp : BaseComponent<GPUModuleComp>,
            IStatProvider, IDefPowerProvider, IDefDescriptionProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new GPUModuleComp());
        }
        private const float DefaultEnergyCost = 0f;
        internal const int DefaultAmount = 4;
        private int amount;
        private float energyCost;

        protected override void OnConfig() {
            amount = Config.GetInt(PropertyIdH.Amount, DefaultAmount);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public bool AddDefInfoAdditional(StringBuilder sb, Def def, bool addedAnything) {
            return addedAnything;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
            ComputerResourcesDefInfo.Append(sb, def, NameHash);
        }

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    enode.ProvideGPU(amount);
                    if (energyCost > 0.0001f) {
                        enode.SetConsumerWantedInput(energyCost);
                    }
                }
            }
        }

        public override string ToString() {
            return $" * GPU [{amount} THz]";
        }

        public string StatsCategoryName => T.ComputerResources;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
            w.Load(IconId.CGPU, T.CpuModuleAmount_V1.F(amount));
        }
    }
}
