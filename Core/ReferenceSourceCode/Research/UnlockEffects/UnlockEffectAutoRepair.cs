using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    public sealed class UnlockEffectAutoRepair : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("AutoRepair",
                new UnlockEffectAutoRepair());
        }
        public string Icon => IconId.CWrench2;
        public string Title => T.ResearchRoboticsAutorepair;
        public string Tooltip => T.ResearchRoboticsAutorepairDesc;

        public void OnUnlock(GameState s, bool notify) {
            if (notify) {
                WPopup.Create(false, Icon, T.UpgradeUnlocked, Tooltip).Spawn();
            }
        }
    }
}
