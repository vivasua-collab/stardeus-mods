using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Path;
using Game.Utils;
using Unity.Mathematics;

namespace Game.Systems.AI.Combat {
    // Movement shared by the two ways a unit closes in without tactics: a drafted unit
    // carrying out the player's attack order, and a bot (AICombatActionAdvance), which never
    // uses cover. The callers differ only in what they do when this gives up.
    public static class CombatApproach {
        public enum Step {
            // walking, or looking for a spot with sight
            Moving,
            // in range with sight, or too close to shoot: the passive attack takes over
            InPosition,
            // no path to the target
            Unreachable,
            // standing next to the target and still unable to hit it (across a wall corner)
            Stuck,
        }

        public enum Bash {
            InProgress,
            CannotBash,
        }

        // Walks toward the target until the unit's best attack can hit it, then stands.
        // Expects sight and distance to be fresh (CombatUnit.IsSightFresh).
        public static Step Towards(GameState S, AIAgentComp agent, CombatUnit cu,
                CombatUnit target) {
            var attack = cu.BestAttack;
            var dist = cu.DistToAttackTarget;
            var rangeMax = attack.Stats.RangeMax.Value;
            if (dist < attack.Stats.RangeMin.Value) {
                // too close to shoot: whatever reaches answers
                return Step.InPosition;
            }
            if (dist <= rangeMax && cu.HasLineOfSight) {
                // failures of an earlier leg don't count against chasing it again
                agent.BB.Byte1 = 0;
                return Step.InPosition;
            }
            float stopDist;
            if (attack.IsMelee) {
                stopDist = math.max(attack.MaxAccuracyDist,
                    math.min(attack.EffectiveRangeMax, rangeMax - 1f));
                if (dist <= stopDist) {
                    // in reach across a wall corner: get closer to find a side that has contact
                    stopDist = dist * 0.5f;
                }
            } else {
                // never closer than the distance a shooter keeps from enemies: halving the
                // distance until sight came walked units into contact, and the punch took over
                var floor = math.max(cu.ClosestRange, attack.Stats.RangeMin.Value);
                if (dist <= rangeMax) {
                    // in range without sight: a spot that has it, or closer in steps until
                    // sight comes (StopOnSight ends the step early)
                    var spot = CastPosition.Find(S, cu, target.PosIdx, target.SightPosition,
                        useCover: false, out _);
                    if (spot > 0) {
                        agent.BB.MoveTargetPos = spot;
                        agent.BB.Object2 = null;
                        if (agent.Motion.TryGoTo(spot, PathReqFlags.AdjOnTop,
                                out agent.BB.MoveProgress)) {
                            return Step.Moving;
                        }
                        agent.Motion.StoreNavFailureAt(spot);
                        agent.BB.ClearMoveTarget();
                    }
                    stopDist = math.max(floor, dist - Tunables.CombatRangedRepositionMaxTiles);
                } else {
                    stopDist = math.max(floor, attack.PreferredDist);
                }
            }
            if (!TryApproach(agent, target, stopDist)) {
                return Step.Unreachable;
            }
            // already as close as it goes and still unable to hit it: nothing to walk, so
            // this would search for a spot every tick
            if (!agent.Motion.IsBusy && agent.BB.Byte1++ > 5) {
                return Step.Stuck;
            }
            return Step.Moving;
        }

        // A walk toward the attack target, marked so StopOnSight can end it early. Cover and
        // back-off walks are not marked: they go somewhere for a reason of their own.
        public static bool TryApproach(AIAgentComp agent, CombatUnit target, float stopDist) {
            agent.BB.MoveTargetPos = target.PosIdx;
            if (!agent.Motion.TryGoTowardsTarget(target.PosIdx, target.Agent.Being, stopDist,
                    out agent.BB.MoveProgress)) {
                agent.BB.Object2 = null;
                return false;
            }
            agent.BB.Object2 = agent.BB.MoveProgress;
            return true;
        }

        // Called while the unit walks. A shooter walking toward its target stops as soon as its
        // gun reaches it with sight from within the distance it prefers: walk legs used to run
        // to their end, a few tiles past the first spot it could fire from.
        public static bool StopOnSight(AIAgentComp agent, CombatUnit cu) {
            if (agent.BB.Object2 == null || agent.BB.Object2 != agent.BB.MoveProgress) {
                return false;
            }
            var attack = cu.BestAttack;
            if (attack == null || attack.IsMelee || !cu.IsSightFresh || !cu.RangedTargetInReach) {
                return false;
            }
            if (cu.DistToAttackTarget > math.max(cu.ClosestRange, attack.PreferredDist)) {
                return false;
            }
            agent.Motion.Clear(resetFlags: true);
            agent.BB.ClearMoveTarget();
            agent.BB.Object2 = null;
            return true;
        }

        /// <summary>
        /// Walks up to and attacks a blocking door/wall found by Positions.FindBlockingDoor
        /// after moving toward the attack target kept failing. Mirrors AICombatActionBreakIn's
        /// own tile handling: attacks the Tile directly via TryAttack, bypassing
        /// cu.CurrentAttack/AttackTarget entirely, so it never disturbs the unit's real combat
        /// target or attack state.
        /// </summary>
        public static Bash ContinueBashingThrough(GameState S, AIAgentComp agent,
                CombatUnit cu, long ticks) {
            var tt = agent.BB.TargetTile;
            if (!tt.IsActive || tt.Health.IsDestroyed || !tt.Constructable.IsConstructed
                    || Positions.NoLongerBlocks(tt, agent.Motion)) {
                // door opened or was destroyed - drop back to normal movement next tick
                agent.BB.TargetTile = null;
                agent.BB.ClearMoveTarget();
                return Bash.InProgress;
            }
            var target = cu.AttackTarget;
            if (target != null && BreachRoute.IsReplanDue(agent, ticks)) {
                var way = BreachRoute.Toward(S, agent, target.PosIdx, out var door);
                if (way == BreachRoute.Way.Open) {
                    // another door opened, or the target came out: walk to it
                    agent.Motion.Clear(resetFlags: true);
                    agent.BB.TargetTile = null;
                    agent.BB.ClearMoveTarget();
                    agent.BB.Byte1 = 0;
                    return Bash.InProgress;
                }
                if (way == BreachRoute.Way.Door && door != tt
                        && BreachRoute.LeadsNowhereNew(S, agent, tt)) {
                    agent.Motion.Clear(resetFlags: true);
                    agent.BB.TargetTile = tt = door;
                    agent.BB.ClearMoveTarget();
                }
            }
            if (agent.BB.MoveProgress?.HasFailed == true) {
                agent.Motion.StoreNavFailureAt(agent.BB.MoveTargetPos, Consts.TicksPer10Minutes);
                agent.BB.ClearMoveTarget();
            }
            if (EntityUtils.IsNearObj(agent.PosIdx, tt)) {
                var bestPunch = cu.BestMeleeAttack(true);
                if (bestPunch == null) {
                    agent.BB.TargetTile = null;
                    return Bash.CannotBash;
                }
                bestPunch.TryAttack(S, cu, tt, bestPunch.MaxAccuracyDist, ticks, 0f, out _, out _);
                return Bash.InProgress;
            }
            if (agent.BB.MoveProgress == null) {
                agent.BB.MoveTargetPos = tt.PosIdx;
                if (!agent.Motion.TryGoTo(tt, PathReqFlags.AdjNearby,
                        out agent.BB.MoveProgress)) {
                    // can't even path next to the door - give up bashing for now
                    agent.BB.TargetTile = null;
                }
            }
            return Bash.InProgress;
        }
    }
}
