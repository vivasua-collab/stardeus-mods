using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class DeviceWarning_DeconstructionInProgress : BaseEntityWarning {
        public const string WarningId = "Device_DeconstructionInProgress";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new DeviceWarning_DeconstructionInProgress());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private Tile tile;

        public override bool ShowFor(GameState s, Tile tile, out bool blockActions) {
            blockActions = false;
            if (!tile.Constructable.IsConstructed) { return false; }
            if (tile.Constructable.TryGetGoal(out var goal)
                    && goal.JobIdH == JobTypeIdH.Demolition) {
                this.tile = tile;
                blockActions = true;
                return true;
            }
            return false;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(IconId.CDemolition, T.InfoDeconstructionInProgress, ShowQuickChoice);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.CancelTask, CancelDeconstruction);
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void CancelDeconstruction() {
            if (tile.Constructable.TryGetGoal(out var goal)) {
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
