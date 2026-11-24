using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Tools;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_GridUnreachable : BaseEntityWarning {
        public const string WarningId = "Device_GridUnreachable";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_GridUnreachable());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;
        public override bool AddWarningOverlay => true;
        public override (string icon, string text) WarningOverlayData => (IconId.CNoConnection, T.EnergyNodeUnreachableInfo);

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = true;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode == null) { return false; }
            if (tile.ENode.IsReachable) { return false; }
            // A node without links is unreachable by definition, Not Connected is
            // the warning that tells the player what to fix
            if (tile.ENode.Node.NumConnections == 0) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CNoConnection, T.EnergyNodeUnreachable, ShowQuickChoice);
            UIUtils.AddTip(life, warning, T.EnergyNodeUnreachableInfo);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(Texts.TitleWithShortcutGlyph(T.ToolConnect,
                ActionType.ShortcutEnergyConnect), ToolConnect.Start);
            qc.AddButton(T.TryAutoConnect, () => EntityUtils.TryAutoConnect(tile));
            qc.AddButton(T.TempConnect, () => {
                tile.ENode?.CreateManageGoal();
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
