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
using Game.Systems.Path;
using Game.Systems.Drama;

namespace Game.Systems.AI {
    public sealed class AIActionChase_Avoid : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionChase_Avoid());
        }
        public const string ActId = "ChaseAvoid";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionChase_Avoid () : base(ActId, () => T.VerbRunningAway) {
            Preconditions = AIState.With(AIVarsH.ChaseAvoided, false);
            Outcomes = AIState.With(AIVarsH.ChaseAvoided, true);
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
            agent.BB.ClearMoveTarget();
            agent.BB.Object2 = null;
            agent.BB.TargetBeing = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            // this will ensure the speed modifier even if we're already moving
            if (!AIActionChase.EnsureTargetBeing(S, agent, goal)) {
                goal.WorldState.Set(AIVarsH.ChaseAvoided, true);
                return Success;
            }
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }
            var chaser = agent.BB.TargetBeing;
            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes &&
                    chaser.AIAgent.CurrentAction?.IdH != AIActionChase.ActIdH) {
                // our date is no longer leading
                D.Warn("Chase is no longer after target {0} -> {1}", chaser, agent);
                goal.WorldState.Set(AIVarsH.ChaseAvoided, true);
                return Success;
            }
            if (Vectors.SqrDistance(chaser.Position, agent.Position) < 2 * 2) {
                AIActionChase.EnsureSpeedMod(agent, chaser, 2.1f);
                // gtfo
                if (AIActionIdleRoam.TryRoam(agent, false, 2, instant: true)) {
                    if (chaser.Motion.IsBusy) {
                        chaser.Motion.Abort(T.NounChase);
                    }
                    // if (!chaser.Motion.TryGoTo(agent.Motion.Request.PosTo,
                    //         PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby |
                    //         PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                    //     D.Warn("Chaser failed to go to where target is going")
                    // }
                }
            }
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
