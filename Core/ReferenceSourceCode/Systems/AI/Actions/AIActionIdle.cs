using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionIdle : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionIdle());
        }
        public const string ActId = "Idle";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionIdle() : base(ActId, () => T.ActNoop) {
            Preconditions = AIState.With(AIVarsH.IsIdle, false);
            Outcomes = AIState.With(AIVarsH.IsIdle, true);
            MarkAsQuiet();
            WithoutRequiredAbilities();
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Type == AIGoalType.Idle) {
                goal.WithInterrupt(0, 0);
            }
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.TicksInCurrentAction <= Consts.TicksPer5Minutes) {
                if (agent.Motion.CanIdle) {
                    return InProgress;
                }
                // if (!S.Sys.Sec.HasThreats && agent.IsFeelingSafeNow && agent.CombatUnit.Squad == null) {
                //     return InProgress;
                // }
            }
            goal.WorldState.Set(AIVarsH.IsIdle, true);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.CanIdle) {
                return Rng.UValue;
            }
            return 1000f;
        }
    }
}
