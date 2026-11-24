using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;

namespace Game.Research {
    public sealed class UnlockEffectMiningAuto : UnlockEffectSetBBKeyBool {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("MiningAuto",
                new UnlockEffectMiningAuto(BBKeyH.MiningAuto));
        }

        public override string Icon => IconId.CDrill;
        public override string Title => T.ResearchMiningautomation;
        public override string Tooltip => T.UnlockMiningautomationDesc_V1.F(
                T.CopterMissions);

        protected override void Notify() {
            // Built here, not at Register: that runs before the language loads
            WPopup.Create(false, IconId.CCopter, T.UpgradeUnlocked,
                T.MiningAutoUnlocked_V1.F(T.OverlayTipCopters)).Spawn();
        }

        private UnlockEffectMiningAuto(int key) : base(key) {
        }
    }
}
