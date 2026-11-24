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
    public sealed class DiskModuleComp : BaseComponent<DiskModuleComp>,
            IStatProvider, IDefPowerProvider, IDefDescriptionProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new DiskModuleComp());
        }
        private const float DefaultEnergyCost = 0f;
        internal const int DefaultAmount = 8;
        private int amount;
        private float energyCost;
        private float compressedEnergyCost;
        private float compression;
        /// <summary>
        /// For Research tutorial
        /// </summary>
        /// <param name="amt"></param>
        public void OverrideAmount(int amt) {
            amount = compressedAmount = amt;
            Tile.ENode.ProvideDisk(amt);
            S.Sig.ElectricityGridChanged.Send();
        }
        public void AddCompression(float amt) {
            if (amt == 0f) {
                D.Err("Trying to add 0 compression!");
                return;
            }
            compression += amt;
            compressedAmount = amount
                + Mathf.RoundToInt(amount * compression);
            Tile.ENode.ProvideDisk(compressedAmount);
            if (energyCost > 0.0001f) {
                compressedEnergyCost = energyCost +
                    Mathf.CeilToInt(energyCost * compression);
                if (Tile.ENode.IsRegistered) {
                    Tile.ENode.SetConsumerWantedInput(compressedEnergyCost);
                }
            }
        }

        protected override void OnConfig() {
            compressedAmount = amount = Config.GetInt(PropertyIdH.Amount, DefaultAmount);
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
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

        public override string ToString() {
            return $" * Storage Module [{amount} ZB]";
        }

        private int compressedAmount;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp enode) {
                    enode.ProvideDisk(compressedAmount);
                    if (energyCost > 0.0001f) {
                        enode.SetConsumerWantedInput(compressedEnergyCost);
                    }
                }
            }
        }

        public string StatsCategoryName => T.ComputerResources;
        public int StatsOrder => 100;

        public void AddStats(Lifetime life, WGroup block, ref int idx) {
            var w = UISpawn.Widget<WIconText>(life, UIPrefabId.WContentText, block.Container, idx++);
            w.Load(IconId.CDisk, T.StorageModuleAmount_V1.F(compressedAmount));
        }
    }
}
