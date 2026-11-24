using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectStockInsights : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("StockInsightsUnlocked",
                new UnlockEffectStockInsights());
        }
        public string Icon => IconId.CPercentGreen;
        public string Title => T.ResearchStockinsights;
        public string Tooltip => T.ResearchStockinsightsDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(T.ResearchStockinsights)).Spawn();
        }
    }
}
