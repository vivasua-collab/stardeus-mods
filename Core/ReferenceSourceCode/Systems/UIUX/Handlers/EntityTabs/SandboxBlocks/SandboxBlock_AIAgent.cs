using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_AIAgent : SandboxBlock {
        public const string BlockId = "AIAgent";
        public override int Order => -80;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_AIAgent());

        public override bool ShowFor(GameState s, BeingData being) {
            return being?.Being?.AIAgent != null;
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var agent = ctx.Being?.Being?.AIAgent;
            if (agent == null) { return; }

            var lifetime = ctx.Lifetime;
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("AI Agent Debug");

            var goalRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            UIUtils.AddTipFunc(lifetime, goalRow,
                () => agent.Goal != null ? D.DumpInstance(agent.Goal) : "No current goal");

            var actionRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);

            var planRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            UIUtils.AddTipFunc(lifetime, planRow,
                () => agent.Plan != null ? D.DumpInstance(agent.Plan) : "No current plan");

            var blackboardRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);
            UIUtils.AddTipFunc(lifetime, blackboardRow,
                () => agent.BB != null ? D.DumpInstance(agent.BB) : "No blackboard");

            var flagsRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block.Container, idx++);

            Refresh(agent, goalRow, actionRow, planRow, blackboardRow, flagsRow);
            agent.ActivityChangedB.Bind(lifetime, () => {
                Refresh(agent, goalRow, actionRow, planRow, blackboardRow, flagsRow);
            }, false);
        }

        private static void Refresh(AIAgentComp agent,
                WIconText2 goalRow, WIconText2 actionRow, WIconText2 planRow,
                WIconText2 blackboardRow, WIconText2 flagsRow) {
            LoadGoal(agent, goalRow);
            LoadAction(agent, actionRow);
            LoadPlan(agent, planRow);
            LoadBlackboard(agent, blackboardRow);
            LoadFlags(agent, flagsRow);
        }

        private static void LoadGoal(AIAgentComp agent, WIconText2 row) {
            var goal = agent.Goal;
            if (goal == null) {
                row.Load(IconId.CCross, "Current Goal", "none");
                return;
            }

            row.Load(goal.GoalIcon,
                $"{goal.Name} [{AIGoal.StateName(goal.State)}]",
                $"{goal.Type} | {goal.JobNameT}");
        }

        private static void LoadAction(AIAgentComp agent, WIconText2 row) {
            var action = agent.CurrentAction;
            if (action == null) {
                row.Load(IconId.CCross, "Current Action", "none");
                return;
            }

            row.Load(IconId.CTime, action.NameT,
                $"{action.Id} | {Units.TicksToTime(agent.TicksInCurrentAction, true)}");
        }

        private static void LoadPlan(AIAgentComp agent, WIconText2 row) {
            var plan = agent.Plan;
            if (plan?.Steps == null) {
                row.Load(IconId.CCross, "Current Plan", "none");
                return;
            }

            var planText = plan.ToString();
            row.Load(IconId.CInfo, string.IsNullOrEmpty(planText) ? "empty" : planText,
                $"Step {plan.StepIdx}/{plan.Steps.Length} | Cost {plan.Cost:F1}");
        }

        private static void LoadBlackboard(AIAgentComp agent, WIconText2 row) {
            var bb = agent.BB;
            if (bb == null) {
                row.Load(IconId.CCross, "Blackboard", "none");
                return;
            }

            row.Load(IconId.CDestination,
                $"Move: {bb.MoveTargetPos}",
                $"Entity:{bb.TargetEntityId} Tile:{bb.TargetTileId} Being:{bb.TargetBeingId} Obj:{bb.TargetObjDataId}");
        }

        private static void LoadFlags(AIAgentComp agent, WIconText2 row) {
            var canWork = agent.Motion != null && agent.CanWork;
            row.Load(IconId.CInfo,
                $"Registered:{agent.IsRegistered} Idle:{agent.IsIdle}",
                $"Disabled:{agent.IsDisabled} CanWork:{canWork}");
        }
    }
#endif
}
