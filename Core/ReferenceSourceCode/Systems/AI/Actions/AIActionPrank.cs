using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using Game.Systems.Stats;
using Game.Systems.Motion;
using Game.Systems.Mental;
using Game.Systems.Drama;

namespace Game.Systems.AI {
    public sealed class AIActionPrank : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionPrank());
        }
        public const string ActId = "Prank";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionPrank() : base(ActId, () => T.VerbPranking) {
            Preconditions = AIState.With(AIVarsH.Pranked, false);
            Outcomes = AIState.With(AIVarsH.Pranked, true);
            WithRequiredJobType(JobTypeIdH.Social);
            RequireMentalHealth();
            WithoutRequiredAbilities();
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // prey
            agent.BB.TargetBeing = null;
            // time spent near prey
            agent.BB.Int1 = 0;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetBeing = null;
            // time spent near prey
            agent.BB.Int1 = 0;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                social.UseSkill();
            }

            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.NounDate}");
            }

            var prey = agent.BB.TargetBeing;

            var distSqr = Vectors.SqrDistance(agent.Position, prey.Position);
            // face each other when possible
            if (!prey.Motion.IsBusy) {
                if (distSqr < 0.25f * 0.25f) {
                    // push the prey a little away from us
                    prey.Motion.TeleportTo(agent.Position + (prey.Position - agent.Position).normalized * 0.33f);
                }
                FacePosition(agent, prey.Position);
            }

            if (distSqr > 0.5f) {
                if (!agent.Motion.TryGoTowards(prey.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                            PathReqFlags.AdjOnTop |
                            PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                    return Failed($"{T.AdRejUnreachable}: {prey.Data.Mind.LinkToShortName}");
                }
            } else if (agent.BB.Int1++ > Consts.TicksPer15Minutes) {
                // The actual result of the prank will be handled by PrankStoryEvent
                goal.WithWorld(AIVarsH.Pranked, true);
                return Success;
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer4Hours) {
                // can't catch the prey
                return Failed($"{T.AdRejUnreachable}: {prey.Data.Mind.LinkToShortName}");
            }

            return InProgress;
        }

        public static bool EnsureTargetBeing(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BB.TargetBeing != null) {
                return true;
            }
            if (goal.TryGetVar(AIGoalVars.BeingId_Uint, out uint dateBeingId)) {
                var prey = S.Query.FindBeingData.Ask(dateBeingId);
                if (prey?.Being == null) {
                    return false;
                }
                agent.BB.TargetBeing = prey.Being;
                return true;
            }
            D.Err("Prank goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
