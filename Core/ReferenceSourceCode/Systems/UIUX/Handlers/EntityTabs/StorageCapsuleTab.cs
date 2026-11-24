using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    // Players look for the order to open a capsule in the entity panel. The bottom bar
    // keeps its button as well, because entity hotkeys (O) press bar buttons.
    public sealed class StorageCapsuleTab : BaseEntityTab {
        public const string TabId = "StorageCapsule";

        [SelfInit]
        private static void Register() {
            RegisterTab(new StorageCapsuleTab());
        }

        private StorageCapsuleComp capsule;
        private GameState S;

        public override string Id => TabId;
        public override int Order => -5;
        public override string TabName => T.StorageCapsule;

        public override bool ShowFor(GameState s, ObjData obj) {
            if (obj.Obj == null) { return false; }
            if (!obj.Obj.TryGetComponent<StorageCapsuleComp>(out var c)) { return false; }
            if (c.IsOpen) { return false; }
            capsule = c;
            S = s;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;
            if (!capsule.HasLanded) {
                LoadInFlight(block, ref idx);
            } else if (capsule.TryGetGoal(out var goal)) {
                LoadPendingTask(block, ref idx, goal);
            } else {
                LoadReady(block, ref idx);
            }
            return true;
        }

        // The button is shown disabled, so players see where the order will be once it lands
        private void LoadInFlight(WContentBlock block, ref int idx) {
            var openBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            // A disabled button barely differs from an enabled one, the grey text tells them apart
            openBtn.Load(IconId.COpenPackage, Texts.Grey(T.OpenCapsule), null,
                () => UISounds.PlayActionDenied());
            openBtn.SetInteractable(false);

            var info = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2TextBlock, block.Container, idx++);
            info.Load(T.StorageCapsuleStillFlying);
        }

        private void LoadPendingTask(WContentBlock block, ref int idx, AIGoal goal) {
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.CurrentTask);

            var s = S;
            var goalUI = AIGoalUI.For(s, goal);
            var taskRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            taskRow.Load(goal.GoalIcon, goal.Name, null,
                () => s.UIQuery.ShowContextPopup.Ask(goalUI, true));
            // A worker taking the task changes its state, not the goal, so the panel
            // is not rebuilt for it
            goal.StateB.Bind(lifetime, st => taskRow.SetText2(AIGoal.StateNameSimple(st)));
            UIUtils.AddTipFunc(lifetime, taskRow, goalUI.ShowInfoTip);
        }

        private void LoadReady(WContentBlock block, ref int idx) {
            var c = capsule;
            var openBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            openBtn.Load(IconId.COpenPackage, T.OpenCapsule, null, () => {
                if (!c.TryOrderOpen()) {
                    UISounds.PlayActionDenied();
                    return;
                }
                ReloadPanel();
                A.S.UIQuery.RebuildEntityActions.Ask();
            });
            openBtn.SetInteractable(!BlockActions);
        }

        protected override void Cleanup() {
            capsule = null;
            S = null;
        }
    }
}
