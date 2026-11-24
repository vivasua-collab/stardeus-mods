using System;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_ReinstallInProgress : BaseEntityWarning {
        public const string WarningId = "Device_ReinstallInProgress";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_ReinstallInProgress());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (tile.Reinstallable == null) { return false;}
            if (tile.Reinstallable.IsPending || tile.Reinstallable.IsMidRelocation) {
                this.tile = tile;
                return true;
            }
            return false;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CMove, T.RelocationPending, ShowQuickChoice);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.CancelTask, CancelRelocation);
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void CancelRelocation() {
            if (tile.Reinstallable.Target?.TryGetGoal(out var goal) == true) {
                AIGoal.Cancel(goal, G.Ticks, T.AdRejCancelled);
            } else if (tile.Reinstallable.TryGetGoal(out var goal2)) {
                AIGoal.Cancel(goal2, G.Ticks, T.AdRejCancelled);
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
