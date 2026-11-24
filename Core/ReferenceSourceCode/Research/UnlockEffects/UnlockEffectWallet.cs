using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectWallet : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("WalletUnlocked",
                new UnlockEffectWallet());
        }
        public string Icon => IconId.CStarcreds;
        public string Title => T.Wallet;
        public string Tooltip => T.UnlockEffectWallet_V1.F(T.OverlayTipWallet);

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.UnlockEffectWallet_V1.F(T.OverlayTipWallet)).Spawn();
        }
    }
}
