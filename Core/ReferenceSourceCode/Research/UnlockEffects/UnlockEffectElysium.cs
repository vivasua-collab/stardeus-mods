using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;

namespace Game.Research {
    public sealed class UnlockEffectElysium : IResearchUnlockEffect {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("ElysiumUnlocked",
                new UnlockEffectElysium());
        }
        public string Icon => IconId.CRobotEye;
        public string Title => T.ResearchElysium;
        public string Tooltip => T.ResearchElysiumDesc;

        public void OnUnlock(GameState s, bool notify) {
            s.BB.Set(BBKeyH.ElysiumAvailable, true);
            if (!notify) { return; }
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ElysiumNowAvailable_V2.F(T.ObjectsDevicesStasisarray,
                    T.StasisElysium)).Spawn();
        }
    }
}
