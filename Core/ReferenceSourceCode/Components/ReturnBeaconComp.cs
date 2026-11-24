using Game.Data;
using Game.Utils;

namespace Game.Components {
    public sealed class ReturnBeaconComp : BaseComponent<ReturnBeaconComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new ReturnBeaconComp());
        }

        public static ReturnBeaconComp FirstAvailable(GameState s) {
            var beacon = s.Components.FindFirstMatching<ReturnBeaconComp>(
                p => p.IsConstructed && !p.Tile.Health.IsDamaged);
            return beacon;
        }

        protected override void OnConfig() {
        }
        public override string ToString() {
            return " * Return Beacon";
        }
    }
}
