using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_MoveInProgress : BaseEntityWarning {
        public const string WarningId = "Entity_MoveInProgress";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_MoveInProgress());
        }
        public override string Id => WarningId;
        public override int Order => 10;
        private MovableComp movable;

        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            if (being.Being?.Movable == null) { return false; }
            if (being.Being.Movable.IsPendingMove) {
                movable = being.Being.Movable;
                return true;
            }
            return false;
        }

        public override bool ShowFor(GameState s, ObjData obj, out bool blockActions) {
            blockActions = false;
            if (obj.Obj?.Movable == null) { return false; }
            if (obj.Obj.Movable.IsPendingMove) {
                movable = obj.Obj.Movable;
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
            if (movable.TryGetGoal(out var g)) {
                AIGoal.Cancel(g, G.Ticks, T.AdRejCancelled);
            } else {
                UISounds.PlayActionDenied();
            }
            A.S.UIQuery.ReloadCurrentEntity.Ask();
            A.S.UIQuery.RebuildEntityActions.Ask();
        }

        public override void Unload() {
            movable = null;
        }
    }
}
