using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
#if !PRODUCTION_BUILD
    public class SandboxBlock_CombatUnit : SandboxBlock {
        public const string BlockId = "CombatUnit";
        public override int Order => -50;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_CombatUnit());

        public override bool ShowFor(GameState s, BeingData being) {
            return being?.Being != null && being.Being.HasComponent<CombatUnitComp>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            var being = ctx.Being;
            if (being?.Being == null || !being.Being.HasComponent<CombatUnitComp>()) {
                return;
            }
            var cu = being.Being.AIAgent?.CombatUnit;
            if (cu == null) { return; }

            var lifetime = ctx.Lifetime;
            var agentGoal = being.Being.AIAgent?.Goal;

            // Block 1: unit overview
            var block1 = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block1.Container, idx++);
            header.Load("Combat Unit Debug");

            var stateRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block1.Container, idx++);
            var suppStr = cu.IsSuppressed
                ? $"Supp: {cu.SuppressionLevel:P0} [SUPPRESSED]"
                : $"Supp: {cu.SuppressionLevel:P0}";
            stateRow.Load(IconId.CAttackTarget,
                $"{cu.StateName} | Cfg: {CombatUnitComp.NameFor(cu.EffectiveConfig)}",
                suppStr);
            UIUtils.AddTipFunc(lifetime, stateRow, () => D.DumpInstance(cu));

            var objectiveRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block1.Container, idx++);
            var army = cu.Army;
            var objectiveStr = cu.Objective != null
                ? $"Objective: {cu.Objective.Id} for {cu.TicksInObjective} ticks"
                : "No objective";
            var armyStr = army != null
                ? $"Army: {army.Units.Count}/{army.InitialUnitCount} | Routing: {army.IsRouting()}"
                : "No army";
            objectiveRow.Load(IconId.CCombat, objectiveStr, armyStr);

            var targetRow = UISpawn.Widget<WIconText2>(lifetime,
                UIPrefabId.WContent2Text2, block1.Container, idx++);
            var targetName = cu.AttackTarget != null ? cu.AttackTarget.EntityName : "—";
            var targetDetail = cu.AttackTarget != null
                ? $"Dist: {cu.DistToAttackTarget:F1} | LOS: {cu.HasLineOfSight} | EnemiesOnMe: {cu.EnemiesTargetingThis}"
                : $"EnemiesOnMe: {cu.EnemiesTargetingThis}";
            targetRow.Load(IconId.CDestination, targetName, targetDetail);

            if (cu.Goals.Count == 0) {
                return;
            }

            // Block 2: combat goals
            var block2 = UISpawn.Block(lifetime, group, gIdx++);
            int gIdx2 = 0;

            var goalsHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block2.Container, gIdx2++);
            goalsHeader.Load($"Combat Goals ({cu.Goals.Count})");

            foreach (var g in cu.Goals) {
                var goalRow = UISpawn.Widget<WIconText2>(lifetime,
                    UIPrefabId.WContent2Text2, block2.Container, gIdx2++);
                var isActive = g == agentGoal;
                var nameStr = isActive ? Texts.Accented(g.Name) : g.Name;
                var stateStr = AIGoal.StateName(g.State);
                var prioStr = $"Im:{g.GoalImportance} Pr:{g.GoalPriority} Cur:{g.CurrentPriority}";
                goalRow.Load(g.GoalIcon, $"{nameStr} [{stateStr}]", prioStr);
                var captured = g;
                UIUtils.AddTipFunc(lifetime, goalRow, () => D.DumpInstance(captured));
            }
        }
    }
#endif
}
