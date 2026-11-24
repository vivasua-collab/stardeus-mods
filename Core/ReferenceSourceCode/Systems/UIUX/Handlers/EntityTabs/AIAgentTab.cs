#if false
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class AIAgentTab : BaseEntityTab {
        public const string TabId = "AIAgent";

        [SelfInit]
        private static void Register() {
            RegisterTab(new AIAgentTab());
        }

        private BeingData being;
        private GameState S;

        public override string Id => TabId;
        public override int Order => 20;
        public override string TabName => T.NounWorker;

        public override bool ShowFor(GameState s, BeingData being) {
            if (being?.Being?.AIAgent == null) { return false; }
            if (!A.InstantBuild && !being.EffectiveFaction.IsPlayer) { return false; }
            S = s;
            this.being = being;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            var agent = being.Being.AIAgent;
            agent.ActivityChangedB.Bind(lifetime, () => ReloadTab(), false);
            int blockIdx = 0;

            // Block 0: Current task
            var taskBlock = UISpawn.Block(lifetime, group, blockIdx++);
            int idx = 0;

            var taskHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, taskBlock.Container, idx++);
            taskHeader.Load(T.CurrentTask);

            var goal = agent.Goal;
            if (goal != null) {
                var goalUI = AIGoalUI.For(S, goal);
                var goalRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContentTextButton, taskBlock.Container, idx++);
                goalRow.Load(goal.GoalIcon, goal.Name, goal.JobNameT,
                    () => S.UIQuery.ShowContextPopup.Ask(goalUI, true));
                UIUtils.AddTipFunc(lifetime, goalRow, () => goalUI.ShowInfoTip());

                var actRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, taskBlock.Container, idx++);
                actRow.Load(IconId.CTime, AIGoal.StateName(goal.State),
                    agent.CurrentAction?.NameT ?? T.Empty);
            } else {
                var noTaskRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, taskBlock.Container, idx++);
                noTaskRow.Load(IconId.CCross, T.NoTask);
            }

            // Block 1: Time stats
            var timeBlock = UISpawn.Block(lifetime, group, blockIdx++);
            idx = 0;

            var timeHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, timeBlock.Container, idx++);
            timeHeader.Load(T.NounTime);

            var counters = being.Counters;

            var workedTotalRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, timeBlock.Container, idx++);
            workedTotalRow.Load(IconId.CTime, T.Working,
                Units.TicksToTime(counters.Get(CounterIdH.TicksWorkedTotal)));
            if (counters.Get(CounterIdH.TicksWorkedLastMonth) > 0) {
                var workedLastMonthRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, timeBlock.Container, idx++);
                workedLastMonthRow.Load(IconId.CTime, $"{T.Working} ({T.LastMonth})",
                    Units.TicksToTime(counters.Get(CounterIdH.TicksWorkedLastMonth)));
            }
            var workedMonthRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, timeBlock.Container, idx++);
            workedMonthRow.Load(IconId.CTime, $"{T.Working} ({T.ThisMonth})",
                Units.TicksToTime(counters.Get(CounterIdH.TicksWorkedMonth)));

            var idleTotalRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, timeBlock.Container, idx++);
            idleTotalRow.Load(IconId.CTime, T.ActNoop,
                Units.TicksToTime(counters.Get(CounterIdH.TicksIdleTotal)));
            if (counters.Get(CounterIdH.TicksIdleLastMonth) > 0) {
                var idleLastMonthRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, timeBlock.Container, idx++);
                idleLastMonthRow.Load(IconId.CTime, $"{T.ActNoop} ({T.LastMonth})",
                    Units.TicksToTime(counters.Get(CounterIdH.TicksIdleLastMonth)));
            }
            var idleMonthRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, timeBlock.Container, idx++);
            idleMonthRow.Load(IconId.CTime, $"{T.ActNoop} ({T.ThisMonth})",
                Units.TicksToTime(counters.Get(CounterIdH.TicksIdleMonth)));

            var movingTotalRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, timeBlock.Container, idx++);
            movingTotalRow.Load(IconId.CTime, T.ActMovetopos,
                Units.TicksToTime(counters.Get(CounterIdH.TicksMovingTotal)));
            if (counters.Get(CounterIdH.TicksMovingLastMonth) > 0) {
                var movingLastMonthRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContentText2, timeBlock.Container, idx++);
                movingLastMonthRow.Load(IconId.CTime, $"{T.ActMovetopos} ({T.LastMonth})",
                    Units.TicksToTime(counters.Get(CounterIdH.TicksMovingLastMonth)));
            }
            var movingMonthRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContentText2, timeBlock.Container, idx++);
            movingMonthRow.Load(IconId.CTime, $"{T.ActMovetopos} ({T.ThisMonth})",
                Units.TicksToTime(counters.Get(CounterIdH.TicksMovingMonth)));

            // Block 2: Carrying (optional)
            var carrying = being.Being.Graphics?.Carrying;
            if (carrying != null) {
                var carryBlock = UISpawn.Block(lifetime, group, blockIdx++);
                idx = 0;
                var carryHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContentHeader, carryBlock.Container, idx++);
                if (carrying is Being carryBeing) {
                    carryHeader.Load(T.CarryingBeing);
                    var capturedBeing = carryBeing;
                    var carryRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                        UIPrefabId.WContentTextButton, carryBlock.Container, idx++);
                    carryRow.Load(null, carryBeing.Data?.Name ?? carryBeing.DefinitionId, null,
                        () => The.R.Sig.SelectEntity.Send(capturedBeing));
                    carryBeing.Data?.Avatar?.Load(carryRow.Image);
                } else if (carrying is Obj carryObj) {
                    carryHeader.Load(T.CarryingObj);
                    var capturedObj = carryObj;
                    var carryRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                        UIPrefabId.WContentTextButton, carryBlock.Container, idx++);
                    carryRow.Load(null, carryObj.MainDataProvider?.EntityName ?? carryObj.DefinitionId, null,
                        () => The.R.Sig.SelectEntity.Send(capturedObj));
                }
            }

            // Block 3: Last completed task
            var compBlock = UISpawn.Block(lifetime, group, blockIdx++);
            idx = 0;
            var compHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, compBlock.Container, idx++);
            compHeader.Load(T.LastCompletedTask);
            if (agent.LastCompletedGoal != null) {
                var compGoal = agent.LastCompletedGoal;
                var compGoalUI = AIGoalUI.For(S, compGoal);
                var compRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContentTextButton, compBlock.Container, idx++);
                compRow.Load(compGoal.GoalIcon, compGoal.Name, compGoal.JobNameT,
                    () => S.UIQuery.ShowContextPopup.Ask(compGoalUI, true));
                UIUtils.AddTipFunc(lifetime, compRow, () => compGoalUI.ShowInfoTip());
            } else {
                var noCompRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, compBlock.Container, idx++);
                noCompRow.Load(IconId.CCross, T.NoTask);
            }

            // Block 4: Last failed task
            var failBlock = UISpawn.Block(lifetime, group, blockIdx++);
            idx = 0;
            var failHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContentHeader, failBlock.Container, idx++);
            failHeader.Load(T.LastFailedTask);
            if (agent.LastFailedGoal != null && !AIGoal.IsCompleted(agent.LastFailedGoal.State)) {
                var failGoal = agent.LastFailedGoal;
                var failGoalUI = AIGoalUI.For(S, failGoal);
                var failRow = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContentTextButton, failBlock.Container, idx++);
                failRow.Load(failGoal.GoalIcon, failGoal.Name, failGoal.JobNameT,
                    () => S.UIQuery.ShowContextPopup.Ask(failGoalUI, true));
                UIUtils.AddTipFunc(lifetime, failRow, () => failGoalUI.ShowInfoTip());
                if (agent.LastFailedGoalReason != null) {
                    var failReasonRow = UISpawn.Widget<WIconText>(lifetime,
                        UIPrefabId.WContentText, failBlock.Container, idx++);
                    failReasonRow.Load(IconId.CQuestion, agent.LastFailedGoalReason);
                }
            } else {
                var noFailRow = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContentText, failBlock.Container, idx++);
                noFailRow.Load(IconId.CCross, T.NoTask);
            }

            return true;
        }

        protected override void Cleanup() {
            being = null;
            S = null;
        }
    }
}
#endif
