using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectStockPrediction : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("StockPredictionUnlocked",
                new UnlockEffectStockPrediction());
        }
        public string Icon => IconId.CPercentGreen;
        public string Title => T.ResearchEconomyMarketprediction;
        public string Tooltip => T.ResearchEconomyMarketpredictionDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(Title)).Spawn();
        }
    }
}
