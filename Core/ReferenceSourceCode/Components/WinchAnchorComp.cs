using Game.Utils;

namespace Game.Components {
    public sealed class WinchAnchorComp : BaseComponent<WinchAnchorComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new WinchAnchorComp());
        }

        public override string ToString() {
            return " * WinchAnchor";
        }
    }
}
