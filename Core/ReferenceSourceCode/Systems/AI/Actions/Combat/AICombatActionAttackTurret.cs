using System;
using System.Buffers;
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
using UnityEngine;
using Unity.Mathematics;
using Game.Systems.Reg;
using UnityEngine.InputSystem;
using Game.Systems.Motion;
using Game.Systems.Stats;
using Game.Systems.Turrets;
using UnityEngine.Pool;
using System.Collections.Generic;

namespace Game.Systems.AI {
    public sealed class AICombatActionAttackTurret : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionAttackTurret());
        }
        public const string ActId = "AttackTurret";
        private AICombatActionAttackTurret() : base(ActId, () => T.CombatGoalAttackTurret) {
            Preconditions = AIState.With(AIVarsH.HasAttackedTurret, false);
            Outcomes = AIState.With(AIVarsH.HasAttackedTurret, true);
            WithoutRequiredAbilities();
            MarkAsCombat();
            MarkAsWork();
        }

        // how many times a unit closes in on a turret it cannot hit yet before it gives up
        private const byte MaxApproachSteps = 8;

        // Ship weapons never fire at beings and often sit out on the hull, where no colonist
        // can reach the raiders hitting them, so a raid could stall there for half a day.
        // Attackers only go after them early in a fight; turrets that shoot beings stay fair
        // game throughout.
        public static bool MayAttack(IAttackableTurret turret, CombatUnit cu, long ticks) {
            return turret.ShootsBeings || MayAttackShipWeapons(cu, ticks);
        }

        public static bool MayAttackShipWeapons(CombatUnit cu, long ticks) {
            var army = cu.Army;
            if (army == null) { return true; }
            return ticks - army.FormedTick
                < Tunables.CombatShipWeaponAttackHours * Consts.TicksPerHour;
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.Byte1 = 0;
            agent.BB.TargetTile = null;
            agent.BB.Object1 = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.TargetTile = null;
            agent.BB.Object1 = null;
            agent.CombatUnit.Attackable = null;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var motion = agent.Motion;
            var cu = agent.CombatUnit;
            if (motion.IsBusy) {
                return InProgress;
            }

            if (agent.BB.MoveProgress?.HasFailed == true) {
                // Cooldown this action
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
                return Failed("Failed finding path towards turret");
            }

            if (!agent.BB.Bool1) {
                agent.BB.Bool1 = true;
                return FindTurret(S, agent, goal, cu, motion);
            }

            if (agent.BB.Object1 is not IAttackableTurret turret) {
                if (agent.BB.TargetTile != null) {
                    turret = agent.BB.TargetTile.GetComponent<IAttackableTurret>();
                } else {
                    // we don't have a turret
                    goal.WorldState.Set(AIVarsH.HasAttackedTurret, true);
                    return Success;
                }
            }

            if (!turret.IsScanning || !MayAttack(turret, cu, ticks)) {
                goal.WorldState.Set(AIVarsH.HasAttackedTurret, true);
                return Success;
            }
            var turretPos = turret.TurretTile.Position;
            var distToTurret = Vectors.Distance(cu.Position, turretPos);
            var attack = cu.BestAttack;
            if (attack?.IsWithinAttackRange(distToTurret, out float accPct, out float distChange) == true) {
                if (EntityUtils.HasLineOfSightCoverFast(cu.SightPosition, turretPos, 0f, attack.Stats.RangeMax.Value)) {
                    if (attack.TryAttack(S, cu, turret.TurretTile, distToTurret, ticks, 0f, out _, out _)) {
                        // D.Warn("Attacking turret with acc {0} and dist sugg {1}", accPct, distChange);
                    }
                    return InProgress;
                }
            }
            // Out of range or out of sight: keep closing in. The first step used to be the only
            // one, after which the unit gave up for 15-30 minutes, and short-range shooters stood
            // under the fire of a turret that outranged them (search goals are off while a
            // turret targets a unit). A failed path ends it at the top of Execute.
            if (attack != null && agent.BB.Byte1 < MaxApproachSteps) {
                agent.BB.Byte1++;
                // the last move finished (a failed one ended the action above)
                agent.BB.MoveProgress = null;
                var tile = turret.TurretTile;
                var approached = attack.IsMelee
                    ? TryGoToOnce(agent, tile)
                    : TryApproachRanged(S, cu, agent, tile) || TryGoToOnce(agent, tile);
                if (approached) {
                    return InProgress;
                }
            }
            agent.BB.TargetTile = null;
            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer15Minutes,
                Consts.TicksPer30Minutes);
            return Failed("Could not reach target turret!");
        }

        private AIActionResult FindTurret(GameState S, AIAgentComp agent,
                AIGoal goal, CombatUnit cu, MotionControl motion) {

            var list = cu.TurretsTargetingThisUnit;
            bool isListPooled = false;
            if (list.Count == 0) {
                list = ListPool<IAttackableTurret>.Get();
                list.Clear();
                isListPooled = true;
                S.Components.FindByInterface(list);
            }

            if (Arrays.IsEmpty(list)) {
                ReturnTurretList(list, isListPooled);
                goal.WorldState.Set(AIVarsH.HasAttackedTurret, true);
                return Success;
            }
            var targetCnt = list.Count;
            if (targetCnt == 0) {
                ReturnTurretList(list, isListPooled);
                return InProgress;
            }
            var tilePool = ArrayPool<IAttackableTurret>.Shared;
            var tiles = tilePool.Rent(targetCnt);
            int i = 0;
            var shipWeaponsAllowed = MayAttackShipWeapons(cu, G.Ticks);
            foreach (IAttackableTurret turret in list) {
                if (turret.IsScanning && (shipWeaponsAllowed || turret.ShootsBeings)) {
                    tiles[i++] = turret;
                }
            }
            // sanity check
            if (!isListPooled && i < list.Count) {
                // not a bug, turret is likely damaged
                // D.Err("BUG: Turrets targeting this unit: {0}. scanning: {1}!", list.Count, i);
                for (int i1 = list.Count - 1; i1 >= 0; i1--) {
                    var tur = list[i1];
                    if (!tur.IsScanning) {
                        // D.Err("Removing inactive turret that is targeting: {0} | {1}",
                        //     tur.TurretTile, tur.TurretTile.Health);
                        list.Remove(tur);
                    }
                }
            }
            IAttackableTurret closestTurret = RegUtils.FindClosestReachable(S, motion.Position.Idx,
                motion.IsGrounded, tiles, i, t => t.TurretTile.PosIdx, onTop: false);
            // D.Warn("Closest from {0}: {1} (gr: {2}). TIles: {3}",
            //     Pos.String(motion.Position.Idx), closestTurret, motion.IsGrounded, D.DumpCollection(tiles));

            if (closestTurret == null) {
                // didn't find a turret
                tilePool.Return(tiles, true);
                ReturnTurretList(list, isListPooled);
                agent.BB.SetCooldown(IdH, G.Ticks,
                    Consts.TicksPer30Minutes, Consts.TicksPer60Minutes);
                return Failed(T.AdRejTargetUnavailable);
            } else {
                // We need to set this tile as an attack target.
                agent.BB.TargetTile = closestTurret.TurretTile;
                agent.BB.Object1 = closestTurret;
                agent.BB.MoveProgress = null;
                agent.CombatUnit.Attackable = closestTurret.TurretTile;

                if (agent.CombatUnit.BestAttack?.IsMelee == false) {
                    // ranged unit!
                    if (!TryApproachRanged(S, cu, agent, closestTurret.TurretTile)) {
                        if (!TryGoToOnce(agent, closestTurret.TurretTile)) {
                            agent.BB.SetCooldown(IdH, G.Ticks,
                                Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
                        }
                    }
                } else {
                    if (!TryGoToOnce(agent, closestTurret.TurretTile)) {
                        agent.BB.SetCooldown(IdH, G.Ticks,
                            Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
                    }
                }
            }
            tilePool.Return(tiles, true);
            ReturnTurretList(list, isListPooled);
            return InProgress;
        }

        private static void ReturnTurretList(List<IAttackableTurret> list, bool isPooled) {
            if (!isPooled) { return; }
            list.Clear();
            ListPool<IAttackableTurret>.Release(list);
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            // ranged attackers will have this very cheap
            if (agent.CombatUnit.BestAttack?.IsMelee == false) {
                return 0.0001f;
            }
            return agent.Motion.GoToCost(Pos.Invalid);
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
        // Like a shooter at a unit (AICombatActionPrepareForAttack.ExecuteRangedAI): fire from
        // where the turret is in reach and sight, otherwise go to a firing spot, otherwise close
        // in a few tiles. Never toward the accuracy peak.
        private bool TryApproachRanged(GameState S, CombatUnit cu, AIAgentComp agent, Tile target) {
            var attack = cu.BestAttack;
            var rangeMax = attack.Stats.RangeMax.Value;
            float distToTarget = Vectors.Distance(cu.Position, target.Position);
            if (distToTarget >= attack.Stats.RangeMin.Value && distToTarget <= rangeMax
                    && !cu.IsInBadPos
                    && EntityUtils.HasLineOfSightCoverFast(cu.SightPosition, target.Position,
                        0f, rangeMax)) {
                return true;
            }
            var spot = CastPosition.Find(S, cu, target.PosIdx, target.Position,
                useCover: cu.IsTactical, out _);
            if (spot > 0) {
                if (agent.Motion.TryGoTo(spot, PathReqFlags.AdjOnTop)) {
                    return true;
                }
                agent.Motion.StoreNavFailureAt(spot);
            }
            if (distToTarget < attack.Stats.RangeMin.Value) {
                // too close and nowhere better to stand
                return false;
            }
            var fraction = math.min(0.25f,
                Tunables.CombatRangedRepositionMaxTiles / math.max(1f, distToTarget));
            return agent.Motion.TryGoTowards(target.PosIdx, fraction);
        }
    }
}
