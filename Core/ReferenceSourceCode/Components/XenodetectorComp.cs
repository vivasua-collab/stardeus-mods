using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Components {
    public sealed class XenodetectorComp : BaseComponent<XenodetectorComp>,
            IRangedShowOthers, IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new XenodetectorComp());
        }
        private const float DefaultEnergyCost = 50f;
        private float range;
        public float Range => range;
        public Color RadiusColor => Consts.ColorTurretRange;
        public bool RadiusSameArea => false;
        public bool RadiusInSpace => true;
        public float ErrorRadius;
        public bool IsScanning;
        public float RangeSqr;

        private float energyCost;

        public int RangeGroupId => 0;
        public float RangeOthersMaxSqrDist => range * range;

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            ErrorRadius = Config.GetFloat(PropertyIdH.ErrorRadius, 5f);
            range = Config.GetInt(PropertyIdH.Range, 30);
            RangeSqr = range * range;
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        public override string ToString() {
            return " * Xenodetector";
        }

        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    this.elNode = elnode;
                    elnode.AddAfterTick(this, AfterTickGrid);
                    elnode.SetConsumerWantedInput(energyCost);
                    AfterTickGrid();
                }
            } else if (message == MsgIdH.ElectricNodeSwitchedOff) {
                IsScanning = false;
            }
        }

        public override void OnLateReady(bool wasLoaded) {
            S.Sys.Sec.Add(this);
        }

        public override void OnRemove() {
            S.Sys.Sec.Remove(this);
        }

        private void AfterTickGrid() {
            IsScanning = elNode.IsConsuming;
        }
    }
}
