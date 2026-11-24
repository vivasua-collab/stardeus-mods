using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.CodeGen;
using UnityEngine;
using KL.Utils;
using Game.Utils;

namespace Game.Systems.AI {
    public sealed class AICombatActionBreakUpFriendlyFight : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionBreakUpFriendlyFight());
        }
        public const string ActId = "BreakUpFriendlyFight";
        private AICombatActionBreakUpFriendlyFight() : base(ActId, () => T.CombatGoalHoldPos) {
            Preconditions = AIState.With(AIVarsH.IsFriendlyFightBrokenUp, false);
            Outcomes = AIState.With(AIVarsH.IsFriendlyFightBrokenUp, true);
            WithoutRequiredAbilities();
            MarkAsRest();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var enemy = agent.CombatUnit.AttackTarget;
            if (enemy != null) {
                var numWounds = agent.CombatUnit.Data.Body.NumHealableWounds;
                var numWoundsEnemy = enemy.Agent.CombatUnit.Data.Body.NumHealableWounds;
                if (numWounds >= numWoundsEnemy) {
                    S.Sys.Log.AddLine(agent.Being,
                        T.FightLost_V1.F(enemy.Data.Mind.LinkToShortName));
                } else {
                    S.Sys.Log.AddLine(enemy.Agent.Being,
                        T.FightLost_V1.F(agent.BeingData.Mind.LinkToShortName));
                }
            }

            agent.CombatUnit.Army.Disband();
            goal.WorldState.Set(AIVarsH.IsFriendlyFightBrokenUp, true);
            return Success;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
