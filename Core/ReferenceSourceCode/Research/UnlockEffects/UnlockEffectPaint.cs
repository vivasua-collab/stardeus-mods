using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;

namespace Game.Research {
    public sealed class UnlockEffectPaint : UnlockEffectSetBBKeyBool {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("PaintUnlocked",
                new UnlockEffectPaint(BBKeyH.PaintUnlocked));
        }
        public override string Icon => IconId.CPaint;
        public override string Title => T.ResearchPaint;
        public override string Tooltip => T.UnlocksItem_V1.F(T.ResearchPaint);

        protected override void Notify() {
            WPopup.Create(false, IconId.CPaint, T.UpgradeUnlocked, T.TintSysUnlocked).Spawn();
        }

        private UnlockEffectPaint(int key) : base(key) { }
    }
}
