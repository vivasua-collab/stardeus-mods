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
using Game.Systems.Creatures;
using Game.Systems.Drama;

namespace Game.Systems.AI {
    public sealed class AIActionChase : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionChase());
        }
        public const string ActId = "Chase";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionChase() : base(ActId, () => T.VerbChasing) {
            Preconditions = AIState.With(AIVarsH.Chased, false);
            Outcomes = AIState.With(AIVarsH.Chased, true);
            WithRequiredJobType(JobTypeIdH.Social);
            RequireMentalHealth();
            WithoutRequiredAbilities();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // speed modifier
            agent.BB.Object2 = null;
            // target
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // target
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
            if (agent.BB.Object2 is StatModifier mod) {
                agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(mod, false);
            }
            agent.BB.Object2 = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.NounTarget}");
            }

            var target = agent.BB.TargetBeing;

            // face each other when possible
            if (!target.Motion.IsBusy) {
                if (Vectors.SqrDistance(agent.Position, target.Position) < 0.25f * 0.25f) {
                    // push the date a little away from us so it wouldn't look creepy
                    target.Motion.TeleportTo(agent.Position + (target.Position - agent.Position).normalized * 0.33f);
                }
                FacePosition(agent, target.Position);
                FacePosition(target.AIAgent, agent.Position, opposite: true);
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes &&
                    target.AIAgent.CurrentAction?.IdH != AIActionChase_Avoid.ActIdH) {
                // our follower is missing
                return Failed($"{T.AdRejTargetUnavailable}: {target.Data.Mind.LinkToShortName}");
            }


            if (agent.TicksInCurrentAction > Consts.TicksPer2Hours) {
                goal.WorldState.Set(AIVarsH.Chased, true);
                return Success;
            }

            EnsureSpeedMod(agent, target, 1.9f);

            // catch
            if (!agent.Motion.TryGoTowardsTarget(target.PosIdx, target, 0.75f)) {
                // D.Err("Failed trying to catch up with chase target {0}->{1}", agent, target);
            }

            return InProgress;
        }

        public static bool EnsureTargetBeing(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BB.TargetBeing != null) {
                return true;
            }
            if (goal.TryGetVar(AIGoalVars.BeingId_Uint, out uint dateBeingId)) {
                var target = S.Query.FindBeingData.Ask(dateBeingId);
                if (target?.Being == null) {
                    return false;
                }
                agent.BB.TargetBeing = target.Being;
                return true;
            }
            D.Err("Chase goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public static void EnsureSpeedMod(AIAgentComp agent, Being target, float speedMul) {
            if (agent.BB.Object2 != null) { return; }
            var targetSpeed = target.Data.StatsBeing.MoveSpeed.Value;
            var ourSpeed = agent.BeingData.StatsBeing.MoveSpeed.Value;
            // half of speed of slowest being
            var wantedSpeed = Mathf.Min(targetSpeed, ourSpeed) * speedMul;
            var mod = StatModifier.Sets(wantedSpeed, T.NounChase);
            if (agent.BeingData.StatsBeing.MoveSpeed.TryAddModifier(mod)) {
                // remember modifier so we can remove it later
                agent.BB.Object2 = mod;
                // D.Warn("Setting move speed mod on: {0}", agent);
            } else {
                D.Err("Failed setting move speed mod on: {0}", agent);
            }
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
