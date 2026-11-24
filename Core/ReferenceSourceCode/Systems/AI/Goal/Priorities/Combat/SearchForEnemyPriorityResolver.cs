using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class SearchForEnemyPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new SearchForEnemyPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            // the undrafted crew fights from where it stands (AICombatActionRespond)
            if (agent.CombatUnit.RespondsInPlace) {
                return false;
            }
            if (agent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            var cfg = agent.CombatUnit.EffectiveConfig;
            if (cfg == CombatUnitConfig.Flee) {
                return false;
            }
            var canAttack = agent.CombatUnit.CanAttack;
            if (!canAttack) {
                return false;
            }
            var cu = agent.CombatUnit;
            if (cu.State == CombatUnitState.Unaware && cu.Data.IsPlayer) {
                return false;
            }
            if (cu.TurretsTargetingThisUnit.Count > 0) {
                return false;
            }
            if (cfg == CombatUnitConfig.Fight) {
                goal.GoalImportance = 2;
            } else {
                goal.GoalImportance = 1;
            }
            if (agent.CombatUnit.AttackTarget != null) {
                // lower priority, but we may still be willing to find a new enemy
                goal.GoalPriority = 2;
                goal.GoalImportance = 1;
                return true;
            }
            var armies = agent.S.Query.GetArmies.Ask();
            var faction = agent.BeingData.EffectiveFaction;
            var isUnitGrounded = agent.Motion.IsGrounded;
            var areas = agent.S.Sys.Areas;
            var uPos = agent.PosIdx;
            var hasUnreachableAreas = areas.HasUnreachableAreas;
            bool foundHostileArmy = false;
            bool hasReachableUnits = false;
            for (int i1 = armies.Count - 1; i1 >= 0; i1--) {
                Army a = armies[i1];
                if (!a.IsDefeated && a.IsHostileTowards(faction)) {
                    foundHostileArmy = true;
                    for (int i = a.Units.Count - 1; i >= 0; i--) {
                        CombatUnit u = a.Units[i];
                        if (u.State == CombatUnitState.Incapacitated) {
                            continue;
                        }
                        if (hasUnreachableAreas && !areas.CheckReachable(uPos, u.PosIdx)) {
                            continue;
                        }
                        if (isUnitGrounded) {
                            if (Pos.SqrDistance(uPos, u.PosIdx) > 15 * 15) {
                                if (!areas.CanReachGrounded(uPos, u.PosIdx)) {
                                    continue;
                                }
                            }
                            hasReachableUnits = true;
                            break;
                        } else {
                            hasReachableUnits = true;
                            break;
                        }
                    }
                    if (hasReachableUnits) {
                        // found a reachable hostile army — high priority
                        goal.GoalPriority = 4;
                        return true;
                    }
                    // this hostile army is unreachable; check remaining armies before giving up
                }
            }
            if (foundHostileArmy) {
                // D.Warn("Unit has no reachable enemies in any hostile army, ignoring combat: {0}", agent);
                return false;
            }
            // no enemies available, but we may want to try to break in somewhere
            goal.GoalImportance = 1;
            goal.GoalPriority = 2;
            return true;
        }

        private SearchForEnemyPriorityResolver() : base(PriorityResolverId.SearchForEnemy) { }

    }
}
