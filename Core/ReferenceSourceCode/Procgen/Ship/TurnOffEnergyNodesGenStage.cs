using Game.Components;
using Game.Constants;

namespace Game.Procgen {
    public class TurnOffEnergyNodesGenStage : BaseShipGenStage {
        public override void Apply() {
            foreach (var en in S.Components.GenFind<ElectricNodeComp>()) {
                en.Switch(false);
            }
        }
    }
}
