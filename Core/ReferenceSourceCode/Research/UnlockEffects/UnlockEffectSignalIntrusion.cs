using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    /// <summary>
    /// Unlocks the Hack action against hostile robotic beings.
    /// Read by Game.Systems.UIUX.HackAction.
    /// </summary>
    public sealed class UnlockEffectSignalIntrusion : UnlockEffectSetBBKeyBool {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("SignalIntrusion",
                new UnlockEffectSignalIntrusion());
        }

        public override string Icon => IconId.CRobotEye;
        public override string Title => T.ResearchRogueaiSignalintrusion;
        public override string Tooltip => T.ResearchRogueaiSignalintrusionDesc;

        protected override void Notify() {
            WPopup.Create(false, Icon, T.UpgradeUnlocked, Tooltip).Spawn();
        }

        private UnlockEffectSignalIntrusion() : base(BBKeyH.SignalIntrusion) { }
    }
}
