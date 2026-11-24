using Game.Components;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class EntityOverview_BodyModSpot : EntityOverviewBlock {
        public const string BlockId = "BodyModSpot";
        public override int Order => 11;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() {
            Register(new EntityOverview_BodyModSpot());
        }

        public override bool ShowFor(GameState s, Tile tile) {
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<BodyModSpotComp>(out var c)) { return false; }
            if (c.HideUI) { return false; }
            return true;
        }

        public override void LoadContent(EntityOverviewContext ctx, WGroup group, ref int gIdx) {
            var tile = ctx.Tile;
            if (tile == null) { return; }
            if (!tile.TryGetComponent<BodyModSpotComp>(out var comp)) { return; }

            var lifetime = ctx.Lifetime;
            comp.StateChangedB.Bind(lifetime, () => A.S.UIQuery.ReloadCurrentEntity.Ask(), false);

            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.NounSurgery);

            var taskName = comp.CurrentTaskName;
            var taskRow = UISpawn.Widget<WIconText>(lifetime,
                UIPrefabId.WContent2Text, block.Container, idx++);
            taskRow.Load(taskName != null ? IconId.CWrench2 : IconId.CInfo,
                taskName ?? T.Idle);
        }

        public override void LoadFooter(EntityOverviewContext ctx, WGroup group, ref int gIdx) {
            var tile = ctx.Tile;
            if (tile == null) { return; }
            if (!tile.TryGetComponent<BodyModSpotComp>(out var comp)) { return; }

            var lifetime = ctx.Lifetime;
            var prio = UISpawn.Widget<WPriorityUpDown>(lifetime,
                UIPrefabId.WContent4Priority, group.Container, gIdx++);
            prio.Up.Load(IconId.WArrowUp, () => {
                comp.MovePriority(1);
                prio.Texts.SetText2(Units.XOutOfY(comp.GoalPriority, 5));
            });
            prio.Down.Load(IconId.WArrowDown, () => {
                comp.MovePriority(-1);
                prio.Texts.SetText2(Units.XOutOfY(comp.GoalPriority, 5));
            });
            prio.Texts.Load(T.TaskPriority, Units.XOutOfY(comp.GoalPriority, 5));
            UIUtils.AddTip(lifetime, prio, T.JobPriorityHint);

            // First-time hub builders could not find where surgeries are ordered
            var surgeryBtn = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, gIdx++);
            surgeryBtn.Load(IconId.CBodyGeneric, T.NounSurgery, () => {
                var patient = comp.Patient;
                if (patient != null) {
                    A.S.UIQuery.ToggleSurgeriesFor.Ask(patient.Data);
                } else if (comp.SlotDesignation == SlotDesignationH.BodyModSpot_Robotic) {
                    A.S.UIQuery.ToggleSurgeriesFiltered.Ask(SurgeriesHandler.FilterWorker);
                } else {
                    A.S.UIQuery.ToggleSurgeries.Ask(!UIShowing.Surgeries);
                }
            });
        }
    }
}
