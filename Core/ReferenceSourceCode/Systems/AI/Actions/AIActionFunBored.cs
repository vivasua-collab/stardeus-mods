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
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionFunBored : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionFunBored());
        }
        public const string ActId = "ActFunBored";
        private AIActionFunBored() : base(ActId, () => T.Bored) {
            Preconditions = AIState.With(AIVarsH.HadFun, false);
            Outcomes = AIState.With(AIVarsH.HadFun, true);
            WithRequiredNeed(NeedIdH.Fun);
            MarkAsQuiet();
            WithoutRequiredAbilities();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                if (agent.IsFeelingSafeNow) {
                    return InProgress;
                }
            }
            if (agent.BB.MoveProgress == null) {
                if (AIActionIdleRoam.TryRoam(agent, true, Rng.URange(1, 3))) {
                    string icon;
                    if (agent.BeingData.StatsBeing.Fun.Value < 0.1f) {
                        icon = IconId.CMoodBad;
                    } else {
                        icon = IconId.CMoodNeutral;
                    }
                    agent.ShowThoughtBubble(icon, T.Bored);
                    return InProgress;
                }
            }

            AddMentalEffect(agent, ticks, Consts.TicksPer8Hours, T.Bored, -10);

            return Failed(T.Bored);
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.HideThoughtBubble();
            agent.BB.ClearMoveTarget();
            agent.Being.Data.StatsBeing.MoveSpeed.RemoveModifier(
                AIActionIdleRoam.StatModRoam, false);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 500f;
        }
    }
}
