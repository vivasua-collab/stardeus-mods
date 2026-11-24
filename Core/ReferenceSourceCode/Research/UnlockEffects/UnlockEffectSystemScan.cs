using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;

using Game.Utils;
namespace Game.Research {
    public sealed class UnlockEffectSystemScan : UnlockEffectSetBBKeyBool {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("SystemScan_Basic",
                new UnlockEffectSystemScan(BBKeyH.SystemScan,
                    () => T.SystemScanBasic,
                    () => T.SystemScanBasicDesc,
                    () => T.SystemScanBasicUnlocked));
            Registries.UnlockEffects.Add("SystemScan_Advanced",
                new UnlockEffectSystemScan(BBKeyH.SystemScanAdvanced,
                    () => T.SystemScanAdvanced,
                    () => T.SystemScanAdvancedDesc,
                    () => T.SystemScanAdvancedUnlocked));
            Registries.UnlockEffects.Add("SystemScan_Extended",
                new UnlockEffectSystemScan(BBKeyH.SystemScanExtended,
                    () => T.SystemScanExtended,
                    () => T.SystemScanExtendedDesc,
                    () => T.SystemScanAdvancedUnlocked));
        }
        public override string Icon => IconId.CCommunications;
        private Func<string> titleFunc;
        private Func<string> tooltipFunc;
        public override string Title => titleFunc();
        public override string Tooltip => tooltipFunc();

        protected override void Notify() {
            WPopup.Create(false, IconId.CCommunications, T.UpgradeUnlocked, notificationFunc()).Spawn();
        }

        private readonly Func<string> notificationFunc;

        private UnlockEffectSystemScan(int key, Func<string> title, Func<string> tooltip,
                Func<string> notification) : base(key) {
            this.notificationFunc = notification;
            this.titleFunc = title;
            this.tooltipFunc = tooltip;
        }
    }
}
