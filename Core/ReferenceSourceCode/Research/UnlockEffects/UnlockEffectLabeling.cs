using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectLabeling : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("Labeling",
                new UnlockEffectLabeling());
        }
        public string Icon => IconId.CLabel;
        public string Title => T.ResearchLabeling;
        public string Tooltip => T.ResearchLabelingDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(T.ResearchLabeling)).Spawn();
        }
    }
}
