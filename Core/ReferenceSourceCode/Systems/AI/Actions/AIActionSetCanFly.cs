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
    public sealed class AIActionSetCanFly : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionSetCanFly());
        }
        public const string ActId = "SetCanFly";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionSetCanFly() : base(ActId, () => T.AbilityFly) {
            Preconditions = AIState.With(AIVarsH.CanFly, false);
            Outcomes = AIState.With(AIVarsH.CanFly, true);
            MarkAsQuiet();
            MarkAsRest();
            WithRequiredAbilities(AbilityIdH.Move, AbilityIdH.Fly);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent,
                AIGoal goal, long ticks) {
            goal.WorldState.Set(AIVarsH.CanFly, true);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 0.001f;
        }
    }
}
