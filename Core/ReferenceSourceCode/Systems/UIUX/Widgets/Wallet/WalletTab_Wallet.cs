using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class WalletTab_Wallet {
        private readonly WalletWidget parent;
        private readonly GameState S;
        private Lifetime lifetime;

        public WalletTab_Wallet(WalletWidget parent, GameState s) {
            this.parent = parent;
            S = s;
        }

        public void Load(Lifetime parentLifetime) {
            lifetime?.Dispose();
            lifetime = parentLifetime.CreateChild("Wallet.Wallet");

            var block = UISpawn.Block(lifetime, parent.WItems, 0);
            int idx = 0;

            // Wallet balance
            var wallet = S.Wallet;
            var balanceW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            if (wallet.IsEmpty) {
                balanceW.Load(IconId.CNoStarcreds, T.WalletIsEmpty, null);
            } else {
                balanceW.Load(IconId.CStarcreds, T.CurrencyStarcreds,
                    Wallet.ToValueString(wallet.Amount));
            }
            wallet.AmountB.Bind(lifetime, amt => {
                if (wallet.IsEmpty) {
                    balanceW.SetImage(IconId.CNoStarcreds);
                    balanceW.SetText(T.WalletIsEmpty);
                    balanceW.SetText2(null);
                } else {
                    balanceW.SetImage(IconId.CStarcreds);
                    balanceW.SetText(T.CurrencyStarcreds);
                    balanceW.SetText2(Wallet.ToValueString(amt));
                }
            });
            UIUtils.AddTip(lifetime, balanceW, T.CurrencyStarcredsDesc);

            // Net worth
            S.Sys.Economy.UpdateNetWorth();
            var netWorthW = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            netWorthW.Load(IconId.CStarcreds, T.NetWorth,
                Wallet.ToValueString(S.Sys.Economy.NetWorthTotal));

            // Mining info
            if (S.Sys.Economy.MiningInfoText != null) {
                var miningW = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block.Container, idx++);
                miningW.Load(IconId.CStarcreds, T.MiningCurrency_V1.F(T.CurrencyStarcreds),
                    S.Sys.Economy.MiningInfoText);
                UIUtils.AddTipFunc(lifetime, miningW, S.Sys.Economy.MiningTooltip);
                S.Sys.Economy.MineRateB.Bind(lifetime, rate => {
                    miningW.SetText2(T.CurrencyMineRateH_V1.F(Wallet.ToValueString(rate)));
                }, notifyImmediately: false);
            }

            // About wallet button
            var aboutBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            aboutBtn.Load(IconId.CInfo, T.AboutWallet, null, () => {
                WPopup.Create(false, IconId.CInfo, T.AboutWallet, T.AboutWalletDesc).Spawn();
            });
            UIUtils.AddTip(lifetime, aboutBtn, T.AboutWalletDesc);

            // Disclaimer button
            var disclaimerBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            disclaimerBtn.Load(IconId.CInfo, T.WalletDisclaimer, null, () => {
                WPopup.Create(false, IconId.CInfo, T.WalletDisclaimer, T.WalletDisclaimerDesc).Spawn();
            });
            UIUtils.AddTip(lifetime, disclaimerBtn, T.WalletDisclaimerDesc);
        }

        public void Cleanup() {
            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
