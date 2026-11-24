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

namespace Game.Systems.AI {
    public sealed class AICombatActionSearchForEnemy : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionSearchForEnemy());
        }

        public const string ActId = "SearchForEnemy";
        public static readonly int CooldownIdH = Hashes.S(ActId);
        public static readonly int PathFailedCooldownIdH = Hashes.S("SearchForEnemyPathFailed");
        private AICombatActionSearchForEnemy() : base(ActId, () => T.CombatStateFindenemy) {
            Preconditions = AIState.With(AIVarsH.HasSearchedForEnemy, false);
            Outcomes = AIState.With(AIVarsH.HasSearchedForEnemy, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsWork();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            agent.BB.ClearMoveTarget();
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            var cu = agent.CombatUnit;
            var target = cu.AttackTarget;
            if (agent.Motion.IsBusy) {
                if (target != null && cu.HasLineOfSight) {
                    agent.Motion.Clear(resetFlags: true);
                } else {
                    return InProgress;
                }
            }


            if (target != null) {
                goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
                return Success;
            }
            var myArmy = cu.Army;
            if (myArmy == null) {
                D.Warn("Army is null while trying to search for enemy: {0}", cu);
                goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
                return Success;
            }

            if (agent.TicksInCurrentAction > Consts.TicksPer2Hours) {
                // chill for a bit
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer30Minutes);
                goal.WorldState.Set(AIVarsH.HasSearchedForEnemy, true);
                return Success;
            }

            var pool = ArrayPool<CombatUnit>.Shared;
            var enemies = BreachRoute.RentEnemyCandidates(S, cu, out var foundEnemies);
            if (foundEnemies == 0) {
                pool.Return(enemies, true);
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer15Minutes, Consts.TicksPer30Minutes);
                return Failed("No enemies");
            }

            // Only enemies the doors let it reach: the nearest one behind a door it may not
            // open failed its path, and BreakIn took over while another enemy stood in plain
            // reach (STA-1737)
            var motion = cu.Agent.Motion;
            CombatUnit randomTarget = RegUtils.FindClosestReachable(
                S, cu.PosIdx, motion.IsGrounded,
                    enemies, foundEnemies, e => e.PosIdx, permissions: motion.Permissions);
            pool.Return(enemies, true);

            if (randomTarget == null) {
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
                return Failed("No reachable enemy!");
            }

            if (agent.BB.MoveProgress?.HasFailed == true) {
                // Cooldown this action
                agent.BB.SetCooldown(IdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
                // the regions saw a way the path did not find: BreakIn must not send the unit
                // straight back here
                agent.BB.SetCooldown(PathFailedCooldownIdH, ticks,
                    Consts.TicksPer10Minutes, Consts.TicksPer15Minutes);
                return Failed("Failed finding path towards enemy");
            }

            float pct = Rng.URange(0.3f, 0.4f);
            var targetPos = randomTarget.PosIdx;
            if (!agent.Motion.TryGoTowards(targetPos, pct, out agent.BB.MoveProgress)) {
                D.ErrSoft("Failed going to the middle of path that was successful! WTF: {0}: {1}",
                    agent.BB.MoveProgress.FailReason, agent);
                // we couldn't find a path to the random unit, how about trying this
                // a few more times for other units in next tick
                if (agent.TicksInCurrentAction > Consts.TicksPer15Minutes) {
                    return Failed("Can't find path to enemy");
                }
            }

            return InProgress;
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.BB.IsCoolingDown(IdH, ticks)) {
                return 0f;
            }
            return agent.Motion.GoToCost(Pos.Invalid);
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }
    }
}
