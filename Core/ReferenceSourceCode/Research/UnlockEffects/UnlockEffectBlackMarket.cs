using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using UnityEngine;

namespace Game.Research {
    public sealed class UnlockEffectBlackMarket : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("BlackMarketUnlocked",
                new UnlockEffectBlackMarket());
        }
        public string Icon => IconId.CBriefcase;
        public string Title => T.ResearchEconomyBlackmarket;
        public string Tooltip => T.ResearchEconomyBlackmarketDesc;

        public void OnUnlock(GameState s, bool notify) {
            s.BB.Set(BBKeyH.BlackMarketAvailable, true);
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(T.ResearchEconomyBlackmarket)).Spawn();
        }
    }
}
