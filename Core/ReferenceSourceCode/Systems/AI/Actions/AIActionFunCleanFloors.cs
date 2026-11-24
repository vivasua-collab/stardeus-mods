using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AIActionFunCleanFloors : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunCleanFloors());
        }
        public const string ActId = "ActFunCleanFloors";
        private AIActionFunCleanFloors() : base(ActId, () => T.ActCleanfloors) {
            Preconditions = AIState.With(AIVarsH.HadFun, false)
                .And(AIVarsH.IsFloorClean, true);
            Outcomes = AIState.With(AIVarsH.HadFun, true);
            WithRequiredNeed(NeedIdH.Fun);
            WithRequiredAbilities(AbilityIdH.CleanFloors, AbilityIdH.DumbLabor);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            return Failed(T.Bored);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.S.Sys.Dirt.IsDirty(agent.PosIdx)) {
                return CachedRandomCostWithCoolDown(agent.BB, ticks, Consts.TicksPerHour);
            }
            return 0f;
        }
    }
}
