using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.AI {
    public sealed class AICombatActionHoldPosition : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionHoldPosition());
        }
        public const string ActId = "HoldPosition";
        private AICombatActionHoldPosition() : base(ActId, () => T.CombatGoalHoldPos) {
            Preconditions = AIState.With(AIVarsH.IsHoldingPosition, false);
            Outcomes = AIState.With(AIVarsH.IsHoldingPosition, true);
            WithoutRequiredAbilities();
            MarkAsCombat();
            MarkAsWork();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var cu = agent.CombatUnit;

            // Melee unit within strike range: hold indefinitely until target dies or unit is critically injured
            if (cu.MeleeInAttackRange) {
                if (cu.Data.Body.VitalityPercent < Tunables.CombatMeleeEscapeHpThreshold) {
                    // Critically injured — allow repositioning/fleeing
                    goal.WorldState.Set(AIVarsH.IsHoldingPosition, true);
                    return Success;
                }
                return InProgress;
            }

            // Melee unit with no reachable target: wait briefly, then let PrepareForAttack kick in
            if (cu.BestAttack?.IsMelee == true) {
                var meleeWaitTicks = (int)(Tunables.CombatMeleeNoTargetWaitMinutes * Consts.TicksPerMinute);
                if (agent.TicksInCurrentAction > meleeWaitTicks) {
                    goal.WorldState.Set(AIVarsH.IsHoldingPosition, true);
                    return Success;
                }
                return InProgress;
            }

            // Ranged unit: time-based hold — longer when in good cover or with LOS
            var targetTime = Consts.TicksPer3Minutes;
            if (cu.HasGoodCover) {
                targetTime += Consts.TicksPer3Minutes;
            }
            if (cu.HasLineOfSight) {
                targetTime += Consts.TicksPer3Minutes;
            } else {
                if (cu.AttackTarget != null) {
                    targetTime -= Consts.TicksPer3Minutes;
                }
            }
            if (agent.TicksInCurrentAction > targetTime) {
                goal.WorldState.Set(AIVarsH.IsHoldingPosition, true);
                return Success;
            }
            return InProgress;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
