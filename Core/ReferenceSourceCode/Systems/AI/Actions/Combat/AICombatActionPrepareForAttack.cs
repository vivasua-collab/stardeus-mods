using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Path;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using Unity.Mathematics;

namespace Game.Systems.AI {
    public sealed class AICombatActionPrepareForAttack : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionPrepareForAttack());
        }
        public const string ActId = "PrepareForAttack";
        /// <summary>
        /// Cooldown set after repeatedly failing to back away from an enemy that's inside
        /// this unit's minimum effective range (cornered / no reachable tile). Checked by
        /// PrepareForAttackPriorityResolver so a stuck unit stops thrashing and fights with
        /// whatever attack it has instead.
        /// </summary>
        public static readonly int BackOffCooldownIdH = Hashes.S("BackOff");
        // AKA Find best position
        private AICombatActionPrepareForAttack() : base(ActId, () => T.CombatGoalFindBestPos) {
            Preconditions = AIState.With(AIVarsH.IsInAttackPosition, false);
            Outcomes = AIState.With(AIVarsH.IsInAttackPosition, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.CombatUnit.AttackTargetHitsMade = 0;
            // how many times in a row did we fail to move
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
            var isUnderDC = agent.DirectControl.IsUnderDirectControl;

            var cu = agent.CombatUnit;

            if (isUnderDC && cu.BestAttack?.IsMelee == false) {
                return ExecuteRangedOrder(S, agent, cu, goal);
            }

            // Actively bashing through a blocking door/wall found below after normal
            // repositioning kept failing - see ContinueBashingThrough.
            if (agent.BB.TargetTile != null) {
                return ContinueBashingThrough(S, agent, cu, ticks);
            }

            if (agent.BB.MoveProgress?.HasFailed == true) {
                // the back-off roam below moves without a MoveTargetPos
                if (agent.BB.MoveTargetPos > 0) {
                    agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos,
                        Consts.TicksPer10Minutes);
                }
                D.Warn("Prepare for attack failed motion: {0} -> {1}",
                    Pos.String(agent.BB.MoveTargetPos), agent);
                agent.BB.ClearMoveTarget();
                if (agent.BB.Byte1++ > 5) {
                    // we failed too many times - look for a blocking door on our own area's
                    // outline (locked or permission-denied) and try to bash/shoot through it
                    // instead of giving up. Not gated to melee: BestMeleeAttack(true) already
                    // falls back to the unit's best ranged weapon when it has no melee attack.
                    var breachTile = Positions.FindBlockingDoor(S, agent);
                    if (breachTile != null) {
                        agent.BB.TargetTile = breachTile;
                        agent.BB.Byte1 = 0;
                        return InProgress;
                    }
                    TryWalkAway(agent);
                    if (!cu.IsAttackTargetForced && cu.HasLineOfSight && cu.BestAttack?.IsMelee == true) {
                        cu.SetAttackTarget(null);
                    }
                    return Failed("Couldn't find good pos to go to!");
                }
            }

            if (!cu.CanAttack) {
                return Failed("Can't attack!");
            }

            // A hostile is inside our own weapon's minimum effective range - back off to
            // where we can actually hit it, regardless of what the accuracy curve says about
            // the current distance. Checked before the completion/range logic below so the
            // unit never reports "in attack position" while something is standing on it.
            if (cu.IsEnemyTooClose && !isUnderDC) {
                if (!agent.BB.IsCoolingDown(BackOffCooldownIdH, ticks)) {
                    // Reuse the same region-local, cover+LOS-validated search the "too close"
                    // cascade further below already relies on (CoverSys.FindCoverFor, backed by
                    // RegionPositionsAt). Unlike a hand-computed step-away-from-threat vector,
                    // this can never leave the unit's own connected walkable area, so it can't
                    // send the unit off through a corridor into a different room.
                    if (Positions.TryFindCover(S, agent, true) || Positions.TryFindCover(S, agent, false)) {
                        return InProgress;
                    }
                    if (agent.BB.Byte1++ > 5) {
                        // cornered - stop thrashing for a while and fight with whatever's in range
                        agent.BB.SetCooldown(BackOffCooldownIdH, ticks,
                            Consts.TicksPer5Minutes, Consts.TicksPer10Minutes);
                    } else {
                        // small bounded step so we're not glued in place while retrying, without
                        // committing to a long, unvalidated walk anywhere
                        AIActionIdleRoam.TryRoam(agent, false, 2);
                    }
                }
                // cooling down, or just failed this attempt: fall through to the normal
                // attack-range handling below rather than getting stuck retrying every tick
            }

            // TODO do this after 1-2 hits
            if (cu.AttackTargetHitsMade > 2 ||
                    agent.TicksInCurrentAction > Consts.TicksPer30Minutes) {
                goal.WorldState.Set(AIVarsH.IsInAttackPosition, true);
                // D.Warn("More tha ntwo attack made or 30 minutes passed! success {0}",
                //     agent);
                return Success;
            }

            var target = cu.AttackTarget;

            if (target == null) {
                if (cu.BestAttack?.IsMelee != true) {
                    Positions.TryFindCover(S, agent, false);
                }
                var noTargetWaitTicks = cu.BestAttack?.IsMelee == true
                    ? (int)(Tunables.CombatMeleeNoTargetWaitMinutes * Consts.TicksPerMinute)
                    : (int)(Tunables.CombatRangedNoTargetWaitMinutes * Consts.TicksPerMinute);
                if (agent.TicksInCurrentAction > noTargetWaitTicks) {
                    return Failed("No target");
                }
                return InProgress;
            }

            var distToTarget = cu.DistToAttackTarget;

            if (cu.BestAttack == null) {
                if (cu.CanAttack) {
                    var bestAtt = cu.SelectAttackMethod(false);
                    if (bestAtt != null) {
                        cu.BestAttack = bestAtt;
                        D.Err("Best attack not set for combat unit that can attack! {0} | unit: {1} | target: {2} | setting best attack on the spot: {3}",
                            goal, cu, target, bestAtt);
                    } else {
                        D.Err("Best attack not set for combat unit that can attack! {0} | unit: {1} | target: {2} | could not find best attack on the spot: {3}",
                            goal, cu, target, D.DumpCollection(agent.BeingData.Body.AttackMethods));
                        return Failed(T.AdjIncapable);
                    }
                } else {
                    return Failed(T.AdjIncapable);
                }
            }

            if (!cu.BestAttack.IsMelee) {
                return ExecuteRangedAI(S, agent, cu, target);
            }

            if (cu.BestAttack.IsWithinAttackRange(distToTarget, out _, out _)) {
                // Melee never retreats to cover - sidestep a bad tile (doorway, crowded
                // pile-up) and stay engaged. A unit under direct control just attacks.
                if (!isUnderDC && cu.IsInBadPos) {
                    Positions.TryLeaveBadCombatPos(S, agent);
                }
                return InProgress;
            }
            var perfectDist = cu.BestAttack.MaxAccuracyDist;
            if (distToTarget < 2f && !isUnderDC) {
                // We are probably trying a diagonal attack and are a little too far. Shuffle
                // will pick a orthogonal spot near the enemy
                if (distToTarget > perfectDist * 1.1f) {
                    cu.TryShuffle(S, target);
                }
                return InProgress;
            }
            // Clamp repositioning to configured max tiles toward target
            var maxTiles = (float)Tunables.CombatMeleeRepositionMaxTiles;
            var fraction = math.min(0.25f, maxTiles / math.max(1f, distToTarget));
            agent.BB.MoveTargetPos = target.PosIdx;
            agent.BB.Object2 = null;
            if (!agent.Motion.TryGoTowards(target.PosIdx, fraction,
                    out agent.BB.MoveProgress)) {
                if (!cu.IsAttackTargetForced && !cu.HasLineOfSight) {
                    cu.SetAttackTarget(null);
                }
                return FailedNavToTarget(agent, target.PosIdx);
            }
            return InProgress;
        }

        // A shooter fires from where it can hit its target and moves only for a firing spot
        // (CastPosition: reach, sight, cover, close to 80% of its range). It never walks to the
        // accuracy peak: that put short guns a few tiles from their targets, where the punch took
        // over, and squeezing the most out of a gun is the player's call with a drafted unit.
        private AIActionResult ExecuteRangedAI(GameState S, AIAgentComp agent, CombatUnit cu,
                CombatUnit target) {
            if (!cu.IsSightFresh) {
                // a new target: sight and distance are measured on the unit's next tick
                return InProgress;
            }
            var canHit = cu.RangedTargetInReach && !cu.IsInBadPos;
            if (canHit && cu.HasCover) {
                return InProgress;
            }
            var spot = CastPosition.Find(S, cu, target.PosIdx, target.SightPosition,
                useCover: true, out var spotScore);
            if (canHit && (spot <= 0
                    || spotScore < CastPosition.ScoreHere(S, cu, target.PosIdx, true)
                        * BetterSpotMul)) {
                // no cover here, but no spot clearly better either
                return InProgress;
            }
            if (spot > 0) {
                agent.BB.MoveTargetPos = spot;
                agent.BB.Object2 = null;
                if (agent.Motion.TryGoTo(spot, PathReqFlags.AdjOnTop,
                        out agent.BB.MoveProgress)) {
                    cu.RecordCoverVisit(spot);
                    return InProgress;
                }
                agent.Motion.StoreNavFailureAt(spot);
                agent.BB.ClearMoveTarget();
            }
            if (canHit) {
                return InProgress;
            }
            // no firing spot within reach: close in toward the distance it prefers, a few tiles
            // at a time, never closer than the distance it keeps from enemies
            var attack = cu.BestAttack;
            var dist = cu.DistToAttackTarget;
            var floor = math.max(cu.ClosestRange, attack.Stats.RangeMin.Value);
            var stopDist = math.max(floor, dist - Tunables.CombatRangedRepositionMaxTiles);
            if (dist > attack.Stats.RangeMax.Value) {
                stopDist = math.max(stopDist, attack.PreferredDist);
            }
            if (!CombatApproach.TryApproach(agent, target, stopDist)) {
                return FailedNavToTarget(agent, target.PosIdx);
            }
            if (!agent.Motion.IsBusy && agent.BB.Byte1++ > 5) {
                // as close as it goes and still no shot: let the other goals have a turn
                return Failed("No spot to fire from");
            }
            return InProgress;
        }

        // How much better a spot with cover must score before a shooter that can already hit
        // its target leaves an uncovered tile for it
        private const float BetterSpotMul = 1.3f;

        // A drafted shooter carrying out the player's attack order: get within range and sight,
        // then stand and let the passive attack fire. None of the AI tactics apply: no cover, no
        // backing off, no door bashing, no walking away. The player put the unit where it is.
        private AIActionResult ExecuteRangedOrder(GameState S, AIAgentComp agent, CombatUnit cu,
                AIGoal goal) {
            var target = cu.AttackTarget;
            if (target == null || !cu.HasDirectAttackOrder) {
                // the order is done, or it is a hold order now: stay put
                goal.WorldState.Set(AIVarsH.IsInAttackPosition, true);
                return Success;
            }
            if (!cu.IsSightFresh) {
                // ordered between ticks: sight and distance are measured on the unit's next tick
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

        // The order can't be carried out: the unit stays where it is and goes back to picking
        // targets it can hit from there, which may be this one again once it comes into range
        private AIActionResult DropOrder(AIAgentComp agent, CombatUnit cu, CombatUnit target) {
            var targetPos = target.PosIdx;
            cu.SetAttackTarget(null);
            return FailedNavToTarget(agent, targetPos);
        }

        // See CombatApproach.ContinueBashingThrough
        private AIActionResult ContinueBashingThrough(GameState S, AIAgentComp agent,
                CombatUnit cu, long ticks) {
            if (CombatApproach.ContinueBashingThrough(S, agent, cu, ticks)
                    == CombatApproach.Bash.CannotBash) {
                return Failed("Can't punch the wall!");
            }
            return InProgress;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
            agent.BB.TargetTile = null;
        }
    }
}
