using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using KL.Grid;
using Game.Systems.Stats;
using Game.Systems.Motion;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionDate_Follow : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionDate_Follow());
        }
        public const string ActId = "DateFollow";
        public readonly static int ActIdH = Hashes.S(ActId);
        private AIActionDate_Follow() : base(ActId, () => T.VerbDating) {
            Preconditions = AIState.With(AIVarsH.HadDateWith_Follow, false);
            Outcomes = AIState.With(AIVarsH.HadDateWith_Follow, true);
            WithRequiredJobType(JobTypeIdH.Social);
            RequireMentalHealth();
            WithoutRequiredAbilities();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // move speed modifier
            agent.BB.Object2 = null;
            agent.BB.TargetBeing = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.Object2 is StatModifier mod) {
                agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(mod, false);
            }
            agent.BB.Object2 = null;
            agent.BB.TargetBeing = null;
            // the lead stands us beside it (AIActionDate_Lead)
            AIActionDate_Lead.SetStandOffset(agent.Being, Vector2.zero);
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            // this will ensure the speed modifier even if we're already moving
            if (!AIActionDate_Lead.EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.NounDate}");
            }
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                social.UseSkill();
            }

            var date = agent.BB.TargetBeing;
            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes &&
                    date.AIAgent.CurrentAction?.IdH != AIActionDate_Lead.ActIdH) {
                // our date is no longer leading
                // D.Warn("Leader is no longer on a date {0} -> {1}", date, agent);
                goal.WorldState.Set(AIVarsH.HadDateWith_Follow, true);
                return Success;
            }
            if (Vectors.SqrDistance(date.Position, agent.Position) > 4f) {
                // catch up with the date
                // D.Warn("Catching up with the date");
                if (!agent.Motion.TryGoTowardsTarget(date.PosIdx, date, 0.5f)) {
                    D.Err("Failed trying to catch up with date leader {0}->{1}", agent, date);
                }
            }
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
