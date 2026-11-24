using Game.Utils;

namespace Game.Components {
    public sealed class InstallableUpgradeComp : BaseComponent<InstallableUpgradeComp> {
        [SelfInit]
        private static void Register() {
            AddComponentPrototype(new InstallableUpgradeComp());
        }

        public override string ToString() {
            return " * Installable Upgrade";
        }
    }
}
