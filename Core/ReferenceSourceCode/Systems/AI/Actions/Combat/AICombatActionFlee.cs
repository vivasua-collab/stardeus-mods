using System;
using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Utils;
using UnityEngine;
using System.Collections.Generic;
using KL.Randomness;
using Game.Systems.Path;

namespace Game.Systems.AI {
    public sealed class AICombatActionFlee : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionFlee());
        }
        public const string ActId = "Flee";
        private AICombatActionFlee() : base(ActId, () => T.CombatStateEscape) {
            Preconditions = AIState.With(AIVarsH.HasFled, false);
            Outcomes = AIState.With(AIVarsH.HasFled, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Int1 = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
            if (agent.BeingData.IsPlayer) {
                agent.S.Sys.Combat.Alerts.OnCrewFled(agent.CombatUnit);
            }
        }
        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Int1 = 0;
            agent.BB.Bool1NotSaved = false;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            // a path request can fail after TryGoTo took it: don't pick that spot again
            if (agent.BB.MoveProgress?.HasFailed == true) {
                if (agent.BB.MoveTargetPos > 0) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos);
                }
                agent.BB.ClearMoveTarget();
            }

            var cu = agent.CombatUnit;
            var cnt = cu.EnemiesTargetingThisUnit.Count;
            if (agent.BB.Int1 == 0) {
                agent.BB.Int1 = Rng.URange(Consts.TicksPer10Minutes, Consts.TicksPer30Minutes);
            }
            // Timeout: we've been fleeing long enough — consider it done.
            if (agent.TicksInCurrentAction > agent.BB.Int1) {
                if (ShouldKeepFleeing(S, agent)) {
                    agent.BB.Int1 = agent.TicksInCurrentAction
                        + Rng.URange(Consts.TicksPer10Minutes, Consts.TicksPer30Minutes);
                    agent.BB.Bool1NotSaved = false;
                    TryFleeMove(S, agent, ticks);
                    return InProgress;
                }
                if (!agent.BB.Bool1NotSaved) {
                    agent.BB.Bool1NotSaved = true;
                    TryWalkAway(agent);
                    return InProgress;
                }
                goal.WithWorld(AIVarsH.HasFled, true);
                return Success;
            }

            float distToAttackerSqr = float.MaxValue;
            CombatUnit closestAttacker = null;
            if (cnt == 0) {
                // nobody is targeting us — find the closest hostile unit to flee from
                closestAttacker = S.Sys.Combat.ClosestStandingEnemy(cu);
                if (closestAttacker == null) {
                    // no enemies anywhere — we're safe
                    if (ShouldKeepFleeing(S, agent)) {
                        TryFleeMove(S, agent, ticks);
                        return InProgress;
                    }
                    goal.WithWorld(AIVarsH.HasFled, true);
                    return Success;
                }
            } else {
                foreach (var attacker in cu.EnemiesTargetingThisUnit) {
                    var d = Pos.SqrDistance(attacker.PosIdx, cu.PosIdx);
                    if (d < distToAttackerSqr) {
                        distToAttackerSqr = d;
                        closestAttacker = attacker;
                    }
                }
            }
            if (closestAttacker == null) {
                if (ShouldKeepFleeing(S, agent)) {
                    TryFleeMove(S, agent, ticks);
                    return InProgress;
                }
                goal.WithWorld(AIVarsH.HasFled, true);
                return Success;
            }
            // the crew runs from an enemy close by (FleePriorityResolver), so a retreat that
            // got it out of reach is done
            if (cu.Data.IsPlayer && !cu.IsThreatNear()) {
                goal.WithWorld(AIVarsH.HasFled, true);
                return Success;
            }
            var attackerPos = closestAttacker.Position;
            // Only humanoid minds take cover (bots never find any)
            if (cu.IsTactical) {
                // if we would be at 0:0, enemy would be at:
                var enemyPosShifted = (attackerPos - cu.Position).normalized;
                // we want to be on the opposite, so let's flip it and make it 5 tiles long
                enemyPosShifted *= -5f;
                // reset the origin back
                var wantedPos = cu.Position + enemyPosShifted;
                if (EntityUtils.IsWithinBounds(wantedPos)) {
                    // cover around the spot opposite the attacker, within half the distance
                    // to it (at least 3 tiles, so an attacker close by still leaves somewhere
                    // to look)
                    var coverRadius = Mathf.Max(3f,
                        Vectors.Distance(attackerPos, cu.Position) * 0.5f);
                    if (Positions.TryFindCoverNear(S, agent, EntityUtils.ToPos(wantedPos),
                            coverRadius)) {
                        return InProgress;
                    }
                }
                // Looks like we can get to move even when we fail to try find cover
                if (agent.Motion.IsBusy) {
                    return InProgress;
                }
            }

            if (TryRetreatFrom(S, agent, closestAttacker.PosIdx, ticks)
                    || IsHoldingSafeSpot(agent)) {
                return InProgress;
            }
            agent.BB.MoveTargetPos = 0;
            if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4))) {
                return InProgress;
            }
            var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
            if (pos != Pos.Invalid) {
                agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop);
            }
            return InProgress;
        }

        // A unit with nowhere farther from the threat to go stays where it is instead of
        // roaming, which used to walk it back toward the enemy
        private static bool IsHoldingSafeSpot(AIAgentComp agent) {
            return agent.BeingData.Safety.IsActuallySafeAt(agent.PosIdx);
        }

        private bool TryRetreatFrom(GameState S, AIAgentComp agent, int threatPos, long ticks) {
            // cornered a moment ago: the search would find nothing again
            if (agent.BB.IsCoolingDown(IdH, ticks)) { return false; }
            var dest = FleeDestination.Find(S, agent, threatPos);
            if (dest == Pos.Invalid) {
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer3Minutes, Consts.TicksPer5Minutes);
                return false;
            }
            agent.BB.MoveTargetPos = dest;
            if (agent.Motion.TryGoTo(dest, PathReqFlags.AdjOnTop, out agent.BB.MoveProgress)) {
                return true;
            }
            agent.Motion.StoreNavFailureAt(dest);
            agent.BB.ClearMoveTarget();
            return false;
        }

        private bool TryFleeMove(GameState S, AIAgentComp agent, long ticks) {
            var threat = S.Sys.Combat.ClosestStandingEnemy(agent.CombatUnit);
            if (threat != null && (TryRetreatFrom(S, agent, threat.PosIdx, ticks)
                    || IsHoldingSafeSpot(agent))) {
                return true;
            }
            agent.BB.MoveTargetPos = 0;
            TryWalkAway(agent);
            if (agent.Motion.IsBusy) {
                return true;
            }
            if (AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4))) {
                return true;
            }
            var pos = AIActionFindSafety.FindSafePos(S, agent, agent.PosIdx);
            if (pos != Pos.Invalid) {
                return agent.Motion.TryGoTo(pos, PathReqFlags.AdjOnTop);
            }
            return false;
        }

        private static bool ShouldKeepFleeing(GameState S, AIAgentComp agent) {
            var cu = agent.CombatUnit;
            return cu.EffectiveConfig == CombatUnitConfig.Flee
                && (!cu.Data.IsPlayer || cu.IsThreatNear())
                && cu.Army != null
                && (cu.Army.Flags & CombatFlags.Friendly) != CombatFlags.Friendly
                && S.Sys.Combat.IsHostileCombatInProgress
                && !agent.Motion.IsImmobilized;
        }


        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 2f;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
