using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectStockTrading : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("StockTradingUnlocked",
                new UnlockEffectStockTrading());
        }
        public string Icon => IconId.CBriefcase;
        public string Title => T.ResearchStocktrading;
        public string Tooltip => T.ResearchStocktradingDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(T.ResearchStocktrading)).Spawn();
        }
    }
}
