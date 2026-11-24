using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectOnlineShop : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("OnlineShopUnlocked",
                new UnlockEffectOnlineShop());
        }
        public string Icon => IconId.CTrade;
        public string Title => T.ResearchEconomyOnlineshop;
        public string Tooltip => T.UnlocksItem_V1.F(T.ResearchEconomyOnlineshop);

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.OnlineShopUnlocked_V1.F(T.ResearchEconomyOnlineshop)).Spawn();
        }
    }
}
