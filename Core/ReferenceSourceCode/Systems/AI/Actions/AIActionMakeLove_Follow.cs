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
    public sealed class AIActionMakeLove_Follow : AIAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AIActionMakeLove_Follow());
        }
        public const string ActId = "MakeLoveFollow";
        public readonly static int ActIdH = Hashes.S(ActId);
        public override SocialChatContext ChatContext => SocialChatContext.NoChat;
        private AIActionMakeLove_Follow() : base(ActId, () => T.VerbDating) {
            Preconditions = AIState.With(AIVarsH.MadeLoveWith_Follow, false);
            Outcomes = AIState.With(AIVarsH.MadeLoveWith_Follow, true);
            MarkAsWork();
            RequireMentalHealth();
            WithoutRequiredAbilities();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // speed modifier
            agent.BB.Object2 = null;
            // stage
            agent.BB.Int1 = 0;
            // lover
            agent.BB.TargetBeing = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // lover
            agent.BB.TargetBeing = null;
            // stage
            agent.BB.Int1 = 0;
            agent.BB.ClearMoveTarget();
            if (agent.BB.Object2 is StatModifier mod) {
                agent.BeingData.StatsBeing.MoveSpeed.RemoveModifier(mod, false);
            }
            agent.BB.Object2 = null;
        }
        public static bool EnsureTargetBeing(GameState S, AIAgentComp agent, AIGoal goal) {
            if (agent.BB.TargetBeing != null) {
                return true;
            }
            if (goal.TryGetVar(AIGoalVars.BeingId_Uint, out uint dateBeingId)) {
                var lover = S.Query.FindBeingData.Ask(dateBeingId);
                if (lover?.Being == null) {
                    return false;
                }
                agent.BB.TargetBeing = lover.Being;
                var loverSpeed = lover.StatsBeing.MoveSpeed.Value;
                var ourSpeed = agent.BeingData.StatsBeing.MoveSpeed.Value;
                // half of speed of slowest being
                var wantedSpeed = Mathf.Min(loverSpeed, ourSpeed) * 0.5f;
                var mod = StatModifier.Sets(wantedSpeed, T.NounDate);
                if (agent.BeingData.StatsBeing.MoveSpeed.TryAddModifier(mod)) {
                    // remember modifier so we can remove it later
                    agent.BB.Object2 = mod;
                    D.Warn("Setting move speed mod on: {0}", agent);
                } else {
                    D.Err("Failed setting move speed mod on: {0}", agent);
                }
                return true;
            }
            D.Err("Date goal does not have a var: {0} | {1}", AIGoalVars.BeingId_Uint, goal);
            return false;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            // this will ensure the speed modifier even if we're already moving
            if (!EnsureTargetBeing(S, agent, goal)) {
                return Failed($"{T.Missing}: {T.NounLover}");
            }

            if (agent.Motion.IsBusy) {
                agent.Motion.Progress.Flags |= MotionProgressFlags.IgnoreCongestion;
                return InProgress;
            }

            var date = agent.BB.TargetBeing;
            if (agent.TicksInCurrentAction > Consts.TicksPer5Minutes &&
                    date.AIAgent.CurrentAction is not IAIActionMakeLove) {
                // our date is no longer leading
                D.Warn("Leader is no longer on a date {0} -> {1}", date, agent);
                goal.WorldState.Set(AIVarsH.MadeLoveWith_Follow, true);
                return Success;
            }
            // if (Vectors.SqrDistance(date.Position, agent.Position) > 4f) {
            //     // catch up with the date
            //     D.Warn("Catching up with the date");
            //     if (!agent.Motion.TryGoTowardsTarget(date.PosIdx, date, 0.5f)) {
            //         D.Err("Failed trying to catch up with date leader {0}->{1}", agent, date);
            //     }
            // }
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }
    }
}
