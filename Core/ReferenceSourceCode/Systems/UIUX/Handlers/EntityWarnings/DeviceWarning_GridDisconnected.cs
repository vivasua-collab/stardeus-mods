using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Tools;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_GridDisconnected : BaseEntityWarning {
        public const string WarningId = "Device_GridDisconnected";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_GridDisconnected());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;
        public override bool AddWarningOverlay => !tile.ENode.IsShipComputer;
        public override (string icon, string text) WarningOverlayData => (IconId.CNoEnergy, T.InfoEnergyNotConnected);

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = true;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode == null || tile.ENode.IsConnected) { return false; }
            if (tile.ENode.HasGoal) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CNoEnergy, T.EnergyNotConnected, ShowQuickChoice);
            UIUtils.AddTip(life, warning, T.ElDeviceDisconnectedDesc);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(Texts.TitleWithShortcutGlyph(T.ToolConnect,
                ActionType.ShortcutEnergyConnect), ToolConnect.Start);
            qc.AddButton(T.TryAutoConnect, () => EntityUtils.TryAutoConnect(tile));
            // if (A.S.Situation.DerelictsApproached > 0) {
                qc.AddButton(T.TempConnect, () => {
                    tile.ENode?.CreateManageGoal();
                    A.S.UIQuery.ReloadCurrentEntity.Ask();
                    A.S.UIQuery.RebuildEntityActions.Ask();
                });
            // }
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public override void Unload() {
            tile = null;
        }
    }
}
