using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;

namespace Game.Systems.AI {
    // The undrafted crew's only way to fight on its own (AICombatActionRespond): only while it
    // has a target, which target selection picks within its reach (CombatUnit.ReachOnItsOwn).
    // Without one the crew works.
    public sealed class RespondPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new RespondPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            var cu = agent.CombatUnit;
            if (agent.DirectControl.IsUnderDirectControl || !cu.RespondsInPlace) {
                return false;
            }
            if (cu.State == CombatUnitState.Unaware || cu.IsRetreating) {
                return false;
            }
            // Ignore fights back only when provoked (a retaliation)
            if (cu.EffectiveConfig != CombatUnitConfig.Fight && !cu.IsRetaliating) {
                return false;
            }
            if (cu.AttackTarget == null || cu.BestAttack == null || !cu.CanAttack) {
                return false;
            }
            goal.GoalImportance = 2;
            goal.GoalPriority = 5;
            return true;
        }

        private RespondPriorityResolver() : base(PriorityResolverId.Respond) { }
    }
}
