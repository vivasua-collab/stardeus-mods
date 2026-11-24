using System;
using Game.Data;
using Game.UI;

namespace Game.Systems.UIUX {
    public sealed class WalletHandler : BaseWidgetHandler<WalletWidget> {
        private readonly WalletContext ctx;
        public WalletHandler(GameState s, WalletWidget widget) : base(s, widget) {
            ctx = new WalletContext();
            widget.Initialize(s, ctx);
            s.UIQuery.ToggleWallet.SetResponder(ToggleWallet);
            s.UIQuery.RebuildWallet.SetResponder(RebuildWallet);
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReloaded);
        }

        public void Cleanup() {
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReloaded);
        }

        private void OnThemeReloaded() {
            widget.OnThemeReloaded();
        }

        private bool RebuildWallet() {
            if (UIShowing.Wallet) {
                widget.Load();
                return true;
            }
            return false;
        }

        private bool ToggleWallet(bool show) {
            widget.Toggle(show);
            return true;
        }
    }
}
