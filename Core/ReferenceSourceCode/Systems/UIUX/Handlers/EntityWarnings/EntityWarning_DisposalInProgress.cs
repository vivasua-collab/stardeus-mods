using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class EntityWarning_DisposalInProgress : BaseEntityWarning {
        public const string WarningId = "Entity_DisposalInProgress";

        [SelfInit]
        private static void Register() {
            RegisterWarning(new EntityWarning_DisposalInProgress());
        }

        public override string Id => WarningId;
        public override int Order => 10;

        private DisposableComp disposable;
        private string icon;
        private string text;

        public override bool ShowFor(GameState s, BeingData being, out bool blockActions) {
            blockActions = false;
            return ShowForInternal(being.Being, out blockActions);
        }

        public override bool ShowFor(GameState s, ObjData obj, out bool blockActions) {
            blockActions = false;
            return ShowForInternal(obj.Obj, out blockActions);
        }

        private bool ShowForInternal(Entity entity, out bool blockActions) {
            blockActions = false;
            if (entity?.TryGetComponent<DisposableComp>(out var comp) != true) {
                return false;
            }
            if (!comp.TryGetGoal(out _)) {
                return false;
            }

            comp.GetButtonData(out icon, out var verb);
            text = $"{T.GoalStateInprogress}: {verb}";
            disposable = comp;
            return true;
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int idx) {
            var warning = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                group.Container, idx++);
            warning.Load(icon, text, ShowQuickChoice);
        }

        private void ShowQuickChoice() {
            var qc = QuickChoice.Create();
            qc.AddButton(T.CancelTask, CancelDisposal);
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void CancelDisposal() {
            if (disposable.TryGetGoal(out var goal)) {
                AIGoal.Cancel(goal, G.Ticks, T.AdRejCancelled);
            } else {
                UISounds.PlayActionDenied();
            }
            A.S.UIQuery.ReloadCurrentEntity.Ask();
            A.S.UIQuery.RebuildEntityActions.Ask();
        }

        public override void Unload() {
            disposable = null;
            icon = null;
            text = null;
        }
    }
}
