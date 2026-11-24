using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Components.Upgrades {
    public sealed class StackerCartridgeUpgrade : GameCartridgeUpgrade {
        private const string upgradeId = "StackerCartridge";
        [SelfInit]
        private static void Register() {
            Add(upgradeId, new StackerCartridgeUpgrade());
        }

        public override string Title => T.ObjUpgradesStackercartridgeupgrade;
        public override string UpgradeId => upgradeId;
        public override string MatTypeId => MatId.StackerCartridgeUpgrade;
        public override string MinigameId => MinigameStacker.StackerId;
    }
}
