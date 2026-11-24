using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using Game.Systems.UIUX.Codex.Pages;

namespace Game.Research {
    public sealed class UnlockEffectDoorPermissions : UnlockEffectSetBBKeyBool {
        [SelfInit]
        private static void Register() {
            Registries.UnlockEffects.Add("DoorPermissionsUnlocked",
                new UnlockEffectDoorPermissions(BBKeyH.DoorPermissionsUnlocked));
        }
        public override string Icon => IconId.CKey;
        public override string Title => T.ResearchSecurityDoorpermissions;
        public override string Tooltip => T.UnlocksItem_V1.F(Title);

        protected override void Notify() {
            A.S.UIQuery.ShowTutorialPopup.Ask(DoorPermissionsPage.PageId, 3f);
            WPopup.Create(false, Icon, T.UpgradeUnlocked, T.ItemNowAvailable_V1.F(Title)).Spawn();
            var doors = The.R.State.Components.AllByType<DoorComp>();
            if (doors == null) { return; }
            foreach (DoorComp d in doors) {
                d.CheckDenyDefaults();
            }
        }

        private UnlockEffectDoorPermissions(int key) : base(key) { }
    }
}
