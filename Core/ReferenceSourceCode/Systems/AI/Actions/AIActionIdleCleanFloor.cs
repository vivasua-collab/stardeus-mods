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
using Game.UI;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionIdleCleanFloor : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionIdleCleanFloor());
        }
        public const string ActId = "IdleCleanFloor";
        private AIActionIdleCleanFloor() : base(ActId, () => T.CleaningFloors) {
            Preconditions = AIState.With(AIVarsH.IsIdle, false)
                .And(AIVarsH.IsFloorClean, true);
            Outcomes = AIState.With(AIVarsH.IsIdle, true);
            WithRequiredAbilities(AbilityIdH.DumbLabor, AbilityIdH.CleanFloors);
            MarkAsWork();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            goal.WithWorld(AIVarsH.IsIdle, true);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            // cleaning for those with clean floors need will be handled by the need
            if (agent.BeingData.HasNeed(NeedIdH.CleanFloors)) {
                return 0f;
            }
            if (agent.BeingData.ConditionsPreventColonyWork) {
                return 0f;
            }
            if (agent.BB.IsCoolingDown(NeedIdH.CleanFloors, ticks)) {
                return 0f;
            }
            if (!agent.Jobs.CanDo(JobTypeIdH.Cleaning, out JobSlot jobSlot)) {
                return 0f;
            }
            float mul = 1f;
            if (!agent.BeingData.IsPlayer) {
                mul = 0f;
            }
            return CachedRandomCost(agent.BB, ticks, Consts.TicksPer2Hours,
                0.0001f * mul, 0.001f * mul);
        }
    }
}
