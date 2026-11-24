using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    // Display only: the quality upgrade itself is wired in CloningPod.json
    // (NoOperatorQualityResearch) and read by CraftingSys.QualityFor
    public sealed class UnlockEffectClonedOrganQuality : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("ClonedOrganQuality",
                new UnlockEffectClonedOrganQuality());
        }
        public string Icon => IconId.CDNA;
        public string Title => T.ResearchLifesupportTissueengineering;
        public string Tooltip => T.ResearchLifesupportTissueengineeringDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (notify) {
                WPopup.Create(false, Icon, T.UpgradeUnlocked, Tooltip).Spawn();
            }
        }
    }
}
