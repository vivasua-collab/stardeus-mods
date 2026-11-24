using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;

namespace Game.Research {
    /// <summary>
    /// Turns pulling a living brain out of a skull from a coin flip into a supported
    /// procedure. Read by BodyPartSlot.EstimateChanceOfSuccess.
    /// </summary>
    public sealed class UnlockEffectCorticalOverride : UnlockEffectSetBBKeyBool {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("CorticalOverride",
                new UnlockEffectCorticalOverride());
        }

        public override string Icon => IconId.CBodyRemoveOne;
        public override string Title => T.ResearchRogueaiCorticaloverride;
        public override string Tooltip => T.ResearchRogueaiCorticaloverrideDesc;

        protected override void Notify() {
            WPopup.Create(false, Icon, T.UpgradeUnlocked, Tooltip).Spawn();
        }

        private UnlockEffectCorticalOverride() : base(BBKeyH.CorticalOverride) { }
    }
}
