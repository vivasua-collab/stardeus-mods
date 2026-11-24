using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using System.Text;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Components {
    public sealed class RadarComp : BaseComponent<RadarComp>, IDefDescriptionProvider,
            IDefPowerProvider {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new RadarComp());
        }

        private const float DefaultEnergyCost = 20f;
        private float energyCost;
        public int ScanStrength;

        protected override void OnConfig() {
            energyCost = Config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost);
            ScanStrength = Config.GetInt(PropertyIdH.ScanStrength);
        }

        public void AddDefPower(Def def, ComponentConfig config, ref DefPower power) {
            power.AddConsumption(config.GetFloat(PropertyIdH.EnergyCost, DefaultEnergyCost));
        }

        private bool isAdded;

        public override void OnLateReady(bool wasLoaded) {
            if (!isAdded) {
                isAdded = true;
                S.Sys.Scan.AddRadar(this);
            }
        }

        public override void OnRemove() {
            if (isAdded) {
                isAdded = false;
                S.Sys.Scan.RemoveRadar(this);
            }
        }


        public override string ToString() {
            return " * Radar";
        }
        public bool IsRadarActive;
        private ElectricNodeComp elNode;

        public override void Receive(IComponent sender, int message) {
            if (message == MsgIdH.ElectricNodeAdded) {
                if (sender is ElectricNodeComp elnode) {
                    elNode = elnode;
                    elnode.SetConsumerWantedInput(energyCost);
                    elnode.AddAfterTick(this, AfterTickGrid);
                    AfterTickGrid();
                }
            // No Scanning For Resources popup on ElectricNodeConnectionChanged any more: the
            // radar connecting is also when BuildARadar completes and the expedition quest is
            // offered, so it interrupted reading that quest. ScanningTutorialTrigger shows it
            // when the player first runs short of something a radar can find instead.
            } else if (message == MsgIdH.ElectricNodeSwitchedOff) {
                IsRadarActive = false;
            }
        }

        public void AfterTickGrid() {
            #if !STARDEUS_DEMO
            if (!elNode.IsConsuming) {
                IsRadarActive = false;
                return;
            }
            if (!elNode.IsPowerable) {
                IsRadarActive = false;
                return;
            }
            if (!elNode.IsReachable) { return; }
            if (Tile.Outdoors.IsOutdoors) {
                S.BB.Set(BBKeyH.RadarPing, G.Ticks);
                IsRadarActive = true;
            } else {
                IsRadarActive = false;
            }
            #endif
        }

        public bool AddDefInfoAdditional(StringBuilder sb,
                Def def, bool addedAnything) {
            var config = def.ComponentConfigFor(NameHash, true);
            if (config == null) { return false; }
            Def.PrependAdditionalInfo(sb, addedAnything);
            var str = config.GetInt(PropertyIdH.ScanStrength);
            sb.AppendFormat("{0}: <b>{1}</b>", T.NounScanStrength, str);
            return true;
        }

        public void AddDefInfo(StringBuilder sb, Def def) {
        }
    }
}
