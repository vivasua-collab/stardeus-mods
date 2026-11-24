using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;

namespace Game.Systems.AI {
    // The bots' only positioning goal (AICombatActionAdvance): go at the target and hit it.
    // Humans use GetInCover, PrepareForAttack and HoldPosition instead (see CombatUnit.IsTactical).
    public sealed class AdvancePriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new AdvancePriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            // the undrafted crew fights from where it stands (AICombatActionRespond)
            if (agent.CombatUnit.RespondsInPlace) {
                return false;
            }
            var cu = agent.CombatUnit;
            var cfg = cu.EffectiveConfig;
            if (cfg == CombatUnitConfig.Flee) {
                return false;
            }
            // Only drafted units with an attack order get here (AISys)
            var isOrdered = agent.DirectControl.IsUnderDirectControl;
            if (!isOrdered) {
                if (cu.State == CombatUnitState.Unaware) {
                    return false;
                }
                if (!cu.CanAttack) {
                    return false;
                }
            }
            // FindEnemy goes looking when there is nobody to go at
            if (cu.AttackTarget == null || cu.BestAttack == null) {
                return false;
            }
            if (isOrdered) {
                goal.GoalImportance = 3;
            } else if (cfg == CombatUnitConfig.Fight) {
                goal.GoalImportance = 2;
            } else {
                goal.GoalImportance = 1;
            }
            // Keep at it while hitting, so FindEnemy and the rest don't pull the bot away
            if (cu.CanHitTargetFromHere) {
                goal.GoalPriority = 5;
            } else {
                goal.GoalPriority = 4;
            }
            return true;
        }

        private AdvancePriorityResolver() : base(PriorityResolverId.Advance) { }
    }
}
