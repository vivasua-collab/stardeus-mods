using System.Collections.Generic;
using System.Text;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Stats;
using Game.Systems.UIUX;

namespace Game.Components {
    public sealed class MemoryModuleComp : BaseComponent<MemoryModuleComp>,
            IStatProvider, IDefPowerProvider, IDefDescriptionProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new MemoryModuleComp());
        }
        private const float DefaultEnergyCost = 0f;
        internal const int DefaultAmount = 4;
        private int amount;
        private float energyCost;
        private float compressedEnergyCost;
        private float compression;
        private BInt compressedAmountB;
        public BInt CompressedAmountB => compressedAmountB ??= new BInt(compressedAmount);
        public void AddCompression(float amt) {
            if (amt == 0f) {
                D.Err("Trying to add 0 compression!");
                return;
            }
            compression += amt;
            compressedAmount = amount
                + Mathf.RoundToInt(amount * compression);
            compressedAmountB?.Set(compressedAmount);
            Tile.ENode.ProvideMemory(compressedAmount);
            if (energyCost > 0.0001f) {
                compressedEnergyCost = energyCost +
                    Mathf.CeilToInt(energyCost * compression);
                Tile.ENode.SetConsumerWantedInput(compressedEnergyCost);
            }
        }

        protected override void OnConfig() {
            compressedAmount = amount = Config.GetInt(PropertyIdH.Amount, DefaultAmount);
            compressedEnergyCost = energyCost = Config.GetFloat(PropertyIdH.EnergyCost,
                DefaultEnergyCost);
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
                    enode.ProvideMemory(compressedAmount);
                    if (energyCost > 0.0001f) {
                        enode.SetConsumerWantedInput(compressedEnergyCost);
                    }
                }
            }
        }

        public override string ToString() {
            return $" * Memory Module [{amount} EB]";
        }
        private int compressedAmount;
        public string StatsCategoryName => T.ComputerResources;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
            CompressedAmountB.Bind(life, (value) => {
                w.Load(IconId.CMemory, T.MemoryModuleAmount_V1.F(value));
            });
        }
    }
}
