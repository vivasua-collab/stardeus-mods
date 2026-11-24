using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Components.Upgrades {
    public sealed class SnakeCartridgeUpgrade : GameCartridgeUpgrade {
        private const string upgradeId = "SnakeCartridge";
        [SelfInit]
        private static void Register() {
            Add(upgradeId, new SnakeCartridgeUpgrade());
        }

        public override string Title => T.ObjUpgradesSnakecartridgeupgrade;
        public override string UpgradeId => upgradeId;
        public override string MatTypeId => MatId.SnakeCartridgeUpgrade;
        public override string MinigameId => MinigameSnake.SnakeId;
    }
}
