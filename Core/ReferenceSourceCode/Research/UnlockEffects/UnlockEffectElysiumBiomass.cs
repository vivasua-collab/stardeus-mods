using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    /// <summary>
    /// Lets Elysium arrays render their sleepers into biomass on top of energy.
    /// Read by StasisArrayMatrixComp.
    /// </summary>
    public sealed class UnlockEffectElysiumBiomass : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("ElysiumBiomass",
                new UnlockEffectElysiumBiomass());
        }

        public string Icon => IconId.CMeat;
        public string Title => T.ResearchRogueaiBiomassharvesting;
        public string Tooltip => T.ResearchRogueaiBiomassharvestingDesc;

        public void OnUnlock(GameState s, bool notify) {
            s.BB.Set(BBKeyH.ElysiumBiomass, true);
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, Tooltip).Spawn();
        }
    }
}
