using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_TurnedOff : BaseEntityWarning {
        public const string WarningId = "Device_TurnedOff";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_TurnedOff());
        }
        public override string Id => WarningId;
        public override int Order => 15;
        private Tile tile;

        public override bool AddWarningOverlay => true;
        public override (string icon, string text) WarningOverlayData => (IconId.CTurnOff, T.InfoEnergyNodeOff);

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode == null || !tile.ENode.IsSwitchable || tile.ENode.IsSwitchedOn) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CTurnOff, T.InfoEnergyNodeOff, ShowQuickChoice);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.SwitchOn, () => {
                tile.ENode.Switch(true);
                A.S.UIQuery.ReloadCurrentEntity.Ask();
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public override void Unload() {
            tile = null;
        }
    }
}
