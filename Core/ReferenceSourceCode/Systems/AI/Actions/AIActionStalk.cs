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
using Game.Systems.Creatures;
using Game.Systems.BodyParts;
using System.Collections.Generic;

namespace Game.Systems.AI {
    public sealed class AIActionStalk : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionStalk());
        }
        public const string ActId = "Stalk";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionStalk() : base(ActId, () => T.VerbStalking) {
            Preconditions = AIState.With(AIVarsH.Stalked, false);
            Outcomes = AIState.With(AIVarsH.Stalked, true);
            MarkAsWork();
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
                return InProgress;
            }

            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed(T.AdRejTargetUnavailable);
            }

            var prey = agent.BB.TargetBeing;

            var distSqr = Vectors.SqrDistance(agent.Position, prey.Position);
            FacePosition(agent, prey.Position);

            if (distSqr > 9f) {
                if (!agent.Motion.TryGoTowards(prey.PosIdx, 0.25f,
                        out agent.BB.MoveProgress,
                            PathReqFlags.AdjOnTop | PathReqFlags.AdjNearby | PathReqFlags.TempDestination | PathReqFlags.Instant)) {
                    return Failed($"{T.AdRejUnreachable}: {prey.Data.Mind.LinkToShortName}");
                }
            }

            if (ticks % 10 == 2 && agent.TicksInCurrentAction > Consts.TicksPer2Hours) {
                var chance = agent.TicksInCurrentAction * 0.1f / Consts.TicksPer6Hours;
                // D.Warn("Chance: {0}", chance);
                if (Rng.UChance(chance)) {
                    goal.WithWorld(AIVarsH.Stalked, true);
                    return Success;

                }
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
            D.Err("Stalking goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
