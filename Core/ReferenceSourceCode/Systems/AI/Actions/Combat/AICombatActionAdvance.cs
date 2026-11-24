using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.AI {
    // How a bot fights: it walks straight at its target, like any other task, and hits it
    // from where it can. No cover, no backing off, no careful steps - that is
    // AICombatActionPrepareForAttack, for humans. A separate action rather than a branch in
    // that one, so the two can't share each other's bugs.
    public sealed class AICombatActionAdvance : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionAdvance());
        }
        public const string ActId = "Advance";
        public static readonly int ActIdH = Hashes.S(ActId);

        private AICombatActionAdvance() : base(ActId, () => T.CombatObjectiveAttack) {
            Preconditions = AIState.With(AIVarsH.IsInStrikePosition, false);
            Outcomes = AIState.With(AIVarsH.IsInStrikePosition, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.CombatUnit.AttackTargetHitsMade = 0;
            // how many times in a row the unit failed to get anywhere
            agent.BB.Byte1 = 0;
            agent.BB.ClearMoveTarget();
            agent.BB.TargetTile = null;
            agent.BB.Object2 = null;
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
            agent.BB.Byte1 = 0;
            agent.BB.TargetTile = null;
            agent.BB.Object2 = null;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy && !CombatApproach.StopOnSight(agent, agent.CombatUnit)) {
                return InProgress;
            }
            var cu = agent.CombatUnit;
            if (agent.DirectControl.IsUnderDirectControl) {
                return ExecuteOrder(S, agent, cu, goal);
            }
            if (cu.EffectiveConfig == CombatUnitConfig.Flee) {
                // combat goals can't interrupt each other: end so Flee can run
                return Done(goal);
            }
            if (agent.BB.TargetTile != null) {
                if (CombatApproach.ContinueBashingThrough(S, agent, cu, ticks)
                        == CombatApproach.Bash.CannotBash) {
                    return Failed("Can't punch the wall!");
                }
                return InProgress;
            }
            if (agent.BB.MoveProgress?.HasFailed == true) {
                if (agent.BB.MoveTargetPos > 0) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos,
                        Consts.TicksPer10Minutes);
                }
                agent.BB.ClearMoveTarget();
                if (agent.BB.Byte1++ > 5) {
                    return GiveUpOrBreakIn(S, agent);
                }
            }
            if (!cu.CanAttack) {
                return Failed("Can't attack!");
            }
            // Let the goals re-evaluate now and then (a new target, a turret, routing)
            if (cu.AttackTargetHitsMade > 2
                    || agent.TicksInCurrentAction > Consts.TicksPer30Minutes) {
                return Done(goal);
            }
            var target = cu.AttackTarget;
            if (target == null) {
                // nothing to go at: FindEnemy takes over
                return Done(goal);
            }
            if (cu.BestAttack == null) {
                return Failed(T.AdjIncapable);
            }
            if (cu.IsSuppressed) {
                // pinned: stands in the open, no new legs
                return InProgress;
            }
            if (!cu.IsSightFresh) {
                // target set between ticks: sight and distance come on the unit's next tick
                return InProgress;
            }
            switch (CombatApproach.Towards(S, agent, cu, target)) {
                case CombatApproach.Step.Unreachable:
                    if (agent.BB.Byte1++ > 5) {
                        return GiveUpOrBreakIn(S, agent);
                    }
                    return InProgress;
                case CombatApproach.Step.Stuck:
                    // next to it and unable to hit it (a wall corner between): step to
                    // another side
                    agent.BB.Byte1 = 0;
                    cu.TryShuffle(S, target);
                    return InProgress;
                default:
                    return InProgress;
            }
        }

        private AIActionResult Done(AIGoal goal) {
            goal.WorldState.Set(AIVarsH.IsInStrikePosition, true);
            return Success;
        }

        // Paths keep failing: a locked door in the way gets bashed, as PrepareForAttack does
        private AIActionResult GiveUpOrBreakIn(GameState S, AIAgentComp agent) {
            var breachTile = Positions.FindBlockingDoor(S, agent);
            if (breachTile != null) {
                agent.BB.TargetTile = breachTile;
                agent.BB.Byte1 = 0;
                return InProgress;
            }
            TryWalkAway(agent);
            return Failed("Can't reach the target!");
        }

        // A drafted bot carrying out the player's attack order, the same way a drafted shooter
        // does in PrepareForAttack: close in, stand, shoot; drop the order when it can't
        private AIActionResult ExecuteOrder(GameState S, AIAgentComp agent, CombatUnit cu,
                AIGoal goal) {
            var target = cu.AttackTarget;
            if (target == null || cu.BestAttack == null || !cu.HasDirectAttackOrder) {
                // the order is done, or it is a hold order now: stay put
                return Done(goal);
            }
            if (!cu.IsSightFresh) {
                return InProgress;
            }
            if (agent.BB.MoveProgress?.HasFailed == true) {
                if (agent.BB.MoveTargetPos > 0) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos,
                        Consts.TicksPer10Minutes);
                }
                agent.BB.ClearMoveTarget();
                if (agent.BB.Byte1++ > 5) {
                    return DropOrder(agent, cu, target);
                }
            }
            switch (CombatApproach.Towards(S, agent, cu, target)) {
                case CombatApproach.Step.Unreachable:
                case CombatApproach.Step.Stuck:
                    return DropOrder(agent, cu, target);
                default:
                    return InProgress;
            }
        }

        private AIActionResult DropOrder(AIAgentComp agent, CombatUnit cu, CombatUnit target) {
            var targetPos = target.PosIdx;
            cu.SetAttackTarget(null);
            return FailedNavToTarget(agent, targetPos);
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
            agent.BB.TargetTile = null;
            agent.BB.Byte1 = 0;
        }
    }
}
