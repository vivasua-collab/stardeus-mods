using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_TempConnection : BaseEntityWarning {
        public const string WarningId = "Device_TempConnection";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_TempConnection());
        }

        public override string Id => WarningId;
        public override int Order => 8;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode == null || !tile.ENode.HasGoal) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CConnected, tile.ENode.Node.HasWorkerLink ?
                T.TempConnectEstablished : T.TempConnectPending, ShowQuickChoice);
        }

        private void ShowQuickChoice() {
            var node = tile?.ENode;
            if (node == null) { return; }
            var qc = QuickChoice.Create();
            qc.AddButton(T.EnergyNodeDisconnect, () => {
                if (node.TryGetGoal(out var goal)) {
                    AIGoal.Cancel(goal, G.Ticks);
                }
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
