using Game.Utils;

namespace Game.Components {
    public sealed class HeatSinkComp : BaseComponent<HeatSinkComp>,
            IShipComputerModule {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new HeatSinkComp());
        }
        protected override void OnConfig() {
        }

        public override string ToString() {
            return " * HeatSink";
        }

        public override void OnLateReady(bool wasLoaded) {
            S.Sig.ShipComputerModuleChanged.Send(this);
        }
    }
}
