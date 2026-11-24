using System;
using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class AICombatActionGetInCover : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionGetInCover());
        }
        public const string ActId = "GetInCover";
        public static readonly int ActIdH = Hashes.S(ActId);
        private AICombatActionGetInCover() : base(ActId, () => T.CombatGoalFindCover) {
            Preconditions = AIState.With(AIVarsH.IsInCover, false);
            Outcomes = AIState.With(AIVarsH.IsInCover, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Byte1 = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
        }
        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Byte1 = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            var cu = agent.CombatUnit;
            if (agent.BB.MoveProgress?.HasFailed == true) {
                if (agent.BB.MoveTargetPos > 0) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos,
                        Consts.TicksPer10Minutes);
                }
                agent.BB.ClearMoveTarget();
                agent.BB.Byte1++;
            }

            if (cu.HasGoodCover && !cu.IsInBadPos) {
                return CompleteWithCooldown(agent, goal, ticks);
            } else if (agent.BB.Bool1NotSaved) {
                // We tried roaming away, we don't care if we are in good cover or not
                // we don't want to get stuck in this action forever, instead let
                // combat AI play it out differently
                return CompleteWithCooldown(agent, goal, ticks);
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer10Minutes) {
                return Failed("could not find cover in 10 mins");
            }

            if (!Positions.TryFindCover(S, agent, true)) {
                if (!Positions.TryFindCover(S, agent, false)) {
                    // Couldn't find proper cover, we'll do one attempt to get out of it
                    agent.BB.Bool1NotSaved = true;
                    if (cu.IsInBadPos) {
                        if (Positions.TryLeaveBadCombatPos(S, agent)) {
                            return InProgress;
                        }
                    }
                    AIActionIdleRoam.TryRoam(agent, false, 2);
                }
            }

            return InProgress;
        }

        private AIActionResult CompleteWithCooldown(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.SetCooldown(ActIdH, ticks,
                Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
            goal.WorldState.Set(AIVarsH.IsInCover, true);
            return Success;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
