using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;
using Game.Systems.Motion;
using Game.Systems.Drama;
using Game.Systems.Mental;

namespace Game.Systems.AI {
    public sealed class AIActionConfessLove : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionConfessLove());
        }
        public const string ActId = "ConfessLove";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.Create(
                SocialContext._LoveConfession,
                faceBuddy: true,
                expectedDuration: Consts.TicksPer30Minutes);
        private AIActionConfessLove() : base(ActId, () => T.VerbConfessingLove) {
            Preconditions = AIState.With(AIVarsH.ConfessedLove, false);
            Outcomes = AIState.With(AIVarsH.ConfessedLove, true);
            MarkAsWork();
            RequireMentalHealth();
            WithoutRequiredAbilities();
            WithRequiredJobType(JobTypeIdH.Social);
            MarkAsMobile();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // prey
            agent.BB.TargetBeing = null;
            // time spent near prey
            agent.BB.Int1 = 0;
            // initiated confession
            agent.BB.Bool1 = false;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.TargetBeing = null;
            // time spent near prey
            agent.BB.Int1 = 0;
            // initiated confession
            agent.BB.Bool1 = false;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.StatusCrush}");
            }

            if (agent.Jobs.TryJob(JobTypeIdH.Social, out var social)) {
                social.UseSkill(3f * SkillXp.Default);
            }

            var prey = agent.BB.TargetBeing;
            if (agent.BB.Bool1) {
                if (prey.AIAgent.CurrentAction?.IdH == AIActionConfessLove_Receive.ActIdH) {
                    FacePosition(agent, prey.Position);
                    FacePosition(prey.AIAgent, agent.Position);
                    // still confessing
                    return InProgress;
                }
                goal.WithWorld(AIVarsH.ConfessedLove, true);
                return Success;
            }

            // The crush may have been picked up (e.g. carried to surgery) since it was chosen,
            // and the confession would pull them out of their carrier's arms (STA-1258)
            if (prey.IsCarriedByAny || prey.Data.Body.IsIncapacitated) {
                return Failed($"{T.AdRejUnreachable}: {T.StatusCrush}");
            }

            var distSqr = Vectors.SqrDistance(agent.Position, prey.Position);
            if (distSqr < 0.25f * 0.25f) {
                if (prey.Motion.IsBusy) {
                    prey.Motion.Abort(T.ReceivedLoveConfession);
                }
                // push the prey a little away from us
                prey.Motion.TeleportTo(agent.Position + (prey.Position - agent.Position).normalized * 0.33f);
                if (!DoConfession(S, agent, prey, ticks)) {
                    goal.WithWorld(AIVarsH.ConfessedLove, true);
                    return Success;
                }
                return InProgress;
            }
            FacePosition(agent, prey.Position);

            if (distSqr > 0.5f) {
                if (!agent.Motion.TryGoTowards(prey.PosIdx, 0.75f,
                        out agent.BB.MoveProgress,
                            PathReqFlags.AdjOnTop |
                            PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                    return Failed($"{T.AdRejUnreachable}: {prey.Data.Mind.LinkToShortName}");
                }
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer8Hours) {
                // can't catch the prey
                return Failed($"{T.AdRejUnreachable}: {T.StatusCrush}");
            }

            return InProgress;
        }

        private bool DoConfession(GameState s, AIAgentComp agent, Being prey, long ticks) {
            var m2 = prey.Data.Mind;
            var g1 = AIGoal.DirectOrder(JobTypeIdH.Task, IconId.CHear,
                    T.VerbReceivingLoveConfession, prey.AIAgent, ticks)
                .WithVar(AIGoalVars.BeingId_Uint, m2.Owner.Id)
                .WithWorldToDesired(AIVarsH.ReceivedLoveConfession, false, true);
            if (!s.Query.ForceGoal.Ask(g1, false)) {
                D.Err("Failed assigning receive confess love goal to: {0}", m2);
                return false;
            } else {
                g1.Flags &= ~AIGoalFlags.ForceAssigned;
            }

            g1.WithInterrupt(1, 0);
            agent.BB.Bool1 = true;
            return true;
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
            D.Err("Confess Love goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
