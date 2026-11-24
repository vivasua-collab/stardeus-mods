using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Components.Upgrades {
    public sealed class BreakoutCartridgeUpgrade : GameCartridgeUpgrade {
        private const string upgradeId = "BreakoutCartridge";
        [SelfInit]
        private static void Register() {
            Add(upgradeId, new BreakoutCartridgeUpgrade());
        }

        public override string Title => T.ObjUpgradesBreakoutcartridgeupgrade;
        public override string UpgradeId => upgradeId;
        public override string MatTypeId => MatId.BreakoutCartridgeUpgrade;
        public override string MinigameId => MinigameBreakout.BreakoutId;
    }
}
