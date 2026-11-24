using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Components.Upgrades {
    public sealed class Game2048CartridgeUpgrade : GameCartridgeUpgrade {
        private const string upgradeId = "Game2048Cartridge";
        [SelfInit]
        private static void Register() {
            Add(upgradeId, new Game2048CartridgeUpgrade());
        }

        public override string Title => T.ObjUpgradesGame2048cartridgeupgrade;
        public override string UpgradeId => upgradeId;
        public override string MatTypeId => MatId.Game2048CartridgeUpgrade;
        public override string MinigameId => Minigame2048.Game2048Id;
    }
}
