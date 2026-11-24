using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectStockMarket : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("StockMarketUnlocked",
                new UnlockEffectStockMarket());
        }
        public string Icon => IconId.CStockMarket;
        public string Title => T.StockMarket;
        public string Tooltip => T.ResearchStockmarketDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(T.StockMarket)).Spawn();
        }
    }
}
