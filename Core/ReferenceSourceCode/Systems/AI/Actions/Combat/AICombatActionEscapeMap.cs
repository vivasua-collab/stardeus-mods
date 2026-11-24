using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Path;

namespace Game.Systems.AI {
    public sealed class AICombatActionEscapeMap : AIAction, IAICombatAction {
        [SelfInit]
        private static void Register() {
            RegisterAction(new AICombatActionEscapeMap());
        }
        public const string ActId = "EscapeMap";
        // escape attempts (path or roam) that may fail in one activation before giving up
        private const int maxFailedAttempts = 5;
        private AICombatActionEscapeMap() : base(ActId, () => T.CombatStateEscape) {
            Preconditions = AIState.With(AIVarsH.HasEscapedMap, false);
            Outcomes = AIState.With(AIVarsH.HasEscapedMap, true);
            WithRequiredAbilities(AbilityIdH.Move);
            MarkAsCombat();
            MarkAsRest();
        }

        public override void OnActivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // D.Err("ESCAPE MAP HAPPENING FOR {0}", agent);
            agent.BB.ClearMoveTarget();
            // how many escape attempts failed
            agent.BB.Byte1 = 0;
        }
        public override void OnDeactivate(AIAgentComp agent, AIGoal goal, long ticks) {
            // D.Err("ESCAPE MAP over FOR {0}", agent);
            agent.BB.ClearMoveTarget();
            agent.BB.Byte1 = 0;
        }

        public override AIActionResult Execute(GameState S, AIAgentComp agent, AIGoal goal, long ticks) {
            if (agent.Motion.IsBusy) {
                return InProgress;
            }

            var cu = agent.CombatUnit;

            if (IsAtMapEdge(agent.PosIdx)) {
                if (!agent.BeingData.EffectiveFaction.IsPlayer) {
                    agent.BeingData.StateFlags |= BeingState.Hidden;
                    var being = agent.Being;
                    S.Clock.OnNextTick(() => S.Beings.Destroy(being));
                }
                goal.WithWorld(AIVarsH.HasEscapedMap, true);
                return Success;
            }

            // TryGoTo accepts a path to a space border tile without knowing whether the
            // unit can get out: a walled in unit only learns it here, from the failed path
            // job, and must not keep requesting the same path forever.
            // No timeout that pretends the unit escaped: it stays on the map, and the combat
            // objective re-arms this goal on its next tick, which restarted the whole loop.
            if (agent.BB.MoveProgress?.HasFailed == true) {
                agent.BB.ClearMoveTarget();
                if (ShouldGiveUp(S, agent)) {
                    return GiveUp(cu);
                }
            }

            var borderIdx = FindBorderTile(S, cu);
            if (borderIdx != Pos.Invalid
                    && agent.Motion.TryGoTo(borderIdx, PathReqFlags.AdjOnTop | PathReqFlags.UnsafeOK,
                        out agent.BB.MoveProgress)) {
                return InProgress;
            }

            // Couldn't head for the border. If the unit is genuinely walled in and
            // cannot reach any map edge, stop fleeing and fight to the death instead
            // of standing still forever.
            if (ShouldGiveUp(S, agent)) {
                return GiveUp(cu);
            }

            AIActionIdleRoam.TryRoam(agent, false, Rng.URange(1, 4), true);
            return InProgress;
        }

        // Called after a failed escape attempt
        private static bool ShouldGiveUp(GameState S, AIAgentComp agent) {
            return !EscapeRoute.CanReachAnyMapEdge(S, agent)
                || ++agent.BB.Byte1 > maxFailedAttempts;
        }

        private AIActionResult GiveUp(CombatUnit cu) {
            cu.GiveUpFleeingAndFight();
            return NeedsReplan("Walled in, cannot escape the map");
        }

        public override float CostFor(AIAgentComp agent, AIGoal goal, long ticks) {
            return 1f;
        }

        public void OnAttackTargetChanged(AIAgentComp agent, AIGoal goal,
                CombatUnit prevTarget, CombatUnit newTarget) {
        }

        private static bool IsAtMapEdge(int posIdx) {
            Pos.ToXY(posIdx, out int x, out int y);
            return x == 0 || x == Pos.GridW - 1 || y == 0 || y == Pos.GridH - 1;
        }

        private static int FindBorderTile(GameState S, CombatUnit cu) {
            var dir = FindFleeDirection(S, cu);
            // Project far enough to guarantee we exit the map in that direction
            float mapDiag = Pos.GridW + Pos.GridH;
            var farPos = cu.Position + dir * mapDiag;
            // Clamp to map bounds — the clamped position will be on the nearest border
            int bx = (int)Mathf.Clamp(farPos.x, 0f, Pos.GridW - 1f);
            int by = (int)Mathf.Clamp(farPos.y, 0f, Pos.GridH - 1f);
            if (bx == 0 && by == 0) {
                bx = 1;
            }
            return Pos.FromXY(bx, by);
        }

        private static Vector2 FindFleeDirection(GameState S, CombatUnit cu) {
            float distToAttacker = float.MaxValue;
            CombatUnit closestAttacker = null;
            var cnt = cu.EnemiesTargetingThisUnit.Count;
            if (cnt == 1) {
                closestAttacker = cu.EnemiesTargetingThisUnit[0];
            } else if (cnt > 1) {
                foreach (var attacker in cu.EnemiesTargetingThisUnit) {
                    var d = Pos.SqrDistance(attacker.PosIdx, cu.PosIdx);
                    if (d < distToAttacker) {
                        distToAttacker = d;
                        closestAttacker = attacker;
                    }
                }
            } else {
                closestAttacker = S.Sys.Combat.ClosestStandingEnemy(cu);
            }
            if (closestAttacker == null) {
                // No enemy found — flee toward nearest map edge from current pos
                return NearestEdgeDirection(cu.PosIdx);
            }
            var away = (cu.Position - closestAttacker.Position);
            if (away.sqrMagnitude < 0.001f) {
                return NearestEdgeDirection(cu.PosIdx);
            }
            return away.normalized;
        }

        private static Vector2 NearestEdgeDirection(int posIdx) {
            Pos.ToXY(posIdx, out int x, out int y);
            int dxLeft = x;
            int dxRight = Pos.GridW - 1 - x;
            int dyBottom = y;
            int dyTop = Pos.GridH - 1 - y;
            int minDist = dxLeft;
            var dir = new Vector2(-1, 0);
            if (dxRight < minDist) { minDist = dxRight; dir = new Vector2(1, 0); }
            if (dyBottom < minDist) { minDist = dyBottom; dir = new Vector2(0, -1); }
            if (dyTop < minDist) { dir = new Vector2(0, 1); }
            return dir;
        }
    }
}
