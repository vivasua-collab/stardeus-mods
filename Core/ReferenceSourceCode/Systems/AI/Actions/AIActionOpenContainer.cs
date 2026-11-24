using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;

namespace Game.Systems.AI {
    public sealed class AIActionOpenContainer : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionOpenContainer());
        }
        public const string ActId = "OpenContainer";
        private AIActionOpenContainer() : base(ActId, () => T.OpenCapsule) {
            Preconditions = AIState
                .With(AIVarsH.IsNear, true)
                .And(AIVarsH.IsContainerOpen, false);

            Outcomes = AIState.With(AIVarsH.IsContainerOpen, true);
            WithRequiredAbilities(AbilityIdH.DumbLabor, AbilityIdH.Carry);
            WithRequiredJobType(JobTypeIdH.Hauling);
            MarkAsMobile();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.HideInfoBar();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (goal.Target is IOpenable openable) {
                if (Pos.SqrDistance(agent.Motion.Position.Idx, openable.Entity.PosIdx) > 2) {
                    return FailedNavToTarget(agent, openable.Entity.PosIdx);
                }
                float timeOpening = ticks - goal.StateStartTick;
                var progress = timeOpening / Consts.TicksPer5Minutes;
                if (progress >= 1f) {
                    openable.Open(agent.Being);
                    goal.WithWorld(AIVarsH.IsContainerOpen, true);
                    return Success;
                } else {
                    agent.ShowInfoBar(Consts.ColorFuelBar, progress, NameT);
                    return InProgress;
                }
            }
            return Error("Wrong target in open goal (not IOpenable) {0}", goal.Target);
        }
    }
}
