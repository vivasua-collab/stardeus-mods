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
using Game.Systems.Reg;
using UnityEngine.InputSystem;
using Game.Systems.Motion;
using Game.Systems.Stats;

namespace Game.Systems.AI {
    public sealed class AICombatActionAttackShipComputer : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionAttackShipComputer());
        }
        public const string ActId = "AttackShipComputer";
        /// <summary>
        /// This action is going to try to break in
        /// </summary>
        private AICombatActionAttackShipComputer() : base(ActId, () => T.CombatGoalAttackCpu) {
            Preconditions = AIState.With(AIVarsH.HasAttackedShipComputer, false);
            Outcomes = AIState.With(AIVarsH.HasAttackedShipComputer, true);
            WithoutRequiredAbilities();
            WithForbiddenFlags(StatFlags.Animal);
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.Bool1 = false;
            agent.BB.TargetTile = null;
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var motion = agent.Motion;
            var cu = agent.CombatUnit;
            var target = cu.AttackTarget;
            if (motion.IsBusy) {
                if (target != null && cu.HasLineOfSight) {
                    return Failed(T.AdRejInterrupted);
                } else {
                    return InProgress;
                }
            }

            if (!agent.BB.Bool1) {
                agent.BB.Bool1 = true;
                return FindCPU(S, agent, goal, cu, motion);
            }

            if (agent.BB.TargetTile != null) {
                if (agent.BB.TargetTile.Health.IsDestroyed || !agent.BB.TargetTile.IsActive) {
                    goal.WorldState.Set(AIVarsH.HasAttackedShipComputer, true);
                    return Success;
                }
                if (EntityUtils.IsNearObj(agent.PosIdx, agent.BB.TargetTile)) {
                    var bestPunch = cu.BestMeleeAttack(true);
                    if (bestPunch == null) {
                        return Failed("Can't punch the wall!");
                    }
                    if (bestPunch.TryAttack(S, cu, agent.BB.TargetTile,
                            bestPunch.MaxAccuracyDist, ticks, 0f, out _, out _)) {
                        // D.Warn("Punched a tile: {0}->{1}", agent.BB.TargetTile.Health,
                        //     agent.BB.TargetTile);
                    }
                    return InProgress;
                } else {
                    // failed to reach
                    agent.BB.TargetTile = null;
                }
            }

            agent.BB.SetCooldown(IdH, ticks, Consts.TicksPer5Minutes, Consts.TicksPer10Minutes);
            return Failed("Could not find CPU!");
        }

        private AIActionResult FindCPU(GameState S, AIAgentComp agent, AIGoal goal, CombatUnit cu, MotionControl motion) {
            var allCPUS = S.Components.AllByType<ShipComputerComp>();
            if (Arrays.IsEmpty(allCPUS)) {
                goal.WorldState.Set(AIVarsH.HasAttackedShipComputer, true);
                return Success;
            }
            var targetCnt = allCPUS.Count;
            if (targetCnt == 0) {
                return InProgress;
            }
            var tilePool = ArrayPool<Tile>.Shared;
            var tiles = tilePool.Rent(targetCnt);
            int i = 0;
            foreach (ShipComputerComp cpu in allCPUS) {
                if (cpu.Tile.IsConstructed && cpu.Tile.IsActive) {
                    tiles[i++] = cpu.Tile;
                }
            }
            Tile closestTile = RegUtils.FindClosestReachable(S, motion.Position.Idx,
                motion.IsGrounded, tiles, i, t => t.PosIdx, onTop: false);

            if (closestTile != null) {
                // We need to set this tile as an attack target.
                agent.BB.TargetTile = closestTile;
                agent.BB.MoveProgress = null;
                TryGoToOnce(agent, closestTile);
            }
            tilePool.Return(tiles, true);
            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            return agent.Motion.GoToCost(Pos.Invalid) * 3f;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
