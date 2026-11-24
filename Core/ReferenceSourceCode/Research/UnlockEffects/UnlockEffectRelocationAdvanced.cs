using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;

namespace Game.Research {
    public sealed class UnlockEffectRelocationAdvanced : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("RelocationAdvanced",
                new UnlockEffectRelocationAdvanced());
        }
        public string Icon => IconId.CMove;
        public string Title => T.ResearchRelocationAdvanced;
        public string Tooltip => T.ResearchRelocationAdvancedDesc;

        public void OnUnlock(GameState s, bool notify) {
            s.BB.Set(BBKeyH.RelocationAdvanced, true);
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(T.ResearchRelocationAdvanced)).Spawn();
        }
    }
}
