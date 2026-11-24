using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_ReplacementPending : BaseEntityWarning {
        public const string WarningId = "Device_ReplacementPending";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_ReplacementPending());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (tile.PendingReplacement?.IsPending != true) { return false; }
            this.tile = tile;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CReconstruct, T.ReplacementInProgress, ShowQuickChoice);
            UIUtils.AddTipFunc(life, warning, () => {
                var sb = Caches.StringBuilder2;
                sb.Clear();
                tile.PendingReplacement.AppendReplacementInfo(sb);
                return sb.ToString();
            });
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.CancelTask, CancelReplacement);
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void CancelReplacement() {
            if (tile.PendingReplacement.TryGetGoal(out var goal)) {
                AIGoal.Cancel(goal, G.Ticks, T.AdRejCancelled);
            } else {
                UISounds.PlayActionDenied();
            }
            A.S.UIQuery.ReloadCurrentEntity.Ask();
            A.S.UIQuery.RebuildEntityActions.Ask();
        }

        public override void Unload() {
            tile = null;
        }
    }
}
