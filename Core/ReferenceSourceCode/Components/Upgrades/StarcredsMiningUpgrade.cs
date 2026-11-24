using Game.Data;
using Game.Constants;
using KL.Utils;
using UnityEngine;
using Game.Data.Economy;
using Game.Achievements;

using Game.Utils;
using Game.Systems.UIUX;
namespace Game.Components.Upgrades {
    public sealed class StarcredsMiningUpgrade : Upgrade {
        public override string Icon => IconId.CStarcreds;
        public override string Title => T.ObjUpgradesStarcredsminingupgrade;
        private const string upgradeId = "StarcredsMining";
        public override string UpgradeId => upgradeId;
        public override string MatTypeId => MatId.StarcredsMiningUpgrade;

        [SelfInit]
        private static void Register() {
            var up = new StarcredsMiningUpgrade();
            Add(up.UpgradeId, up);
        }


        public override bool IsCompatibleWith(Entity entity) {
            if (entity.TryGetComponent<CurrencyMinerComp>(out var miner)) {
                return !miner.IsMinerAdded;
            }
            return false;
        }

        public override void OnInstall(Entity entity) {
            var miner = entity.GetComponent<CurrencyMinerComp>();
            The.AchievementTracker.Unlock(AchievementCrypto.AchievementId);
            miner.RegisterMiner();
            if (!Ready.Game) { return; }
            var economy = entity.S.Sys.Economy;
            economy.RefreshMiningRate();
            WPopup.Create(false, IconId.CStarcreds,
                T.MiningCurrency_V1.F(T.CurrencyStarcreds),
                economy.MiningInfoText,
                economy.MiningTooltip())
                .WithAction(T.Wallet, () => A.S.UIQuery.ToggleWallet.Ask(true))
                .WithAction2(T.Ok, null)
                .Spawn();
        }

        public override void OnUninstall(Entity entity) {
            var miner = entity.GetComponent<CurrencyMinerComp>();
            miner.UnregisterMiner();
        }
    }
}
