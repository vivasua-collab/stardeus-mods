using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class WalletContext {
        public string SelectedTab;
        public Lifetime MainLifetime;

        public void ShowTab(string name) {
            if (SelectedTab == name) { return; }
            SelectedTab = name;
            A.S.UIQuery.RebuildWallet.Ask();
        }
    }
}
