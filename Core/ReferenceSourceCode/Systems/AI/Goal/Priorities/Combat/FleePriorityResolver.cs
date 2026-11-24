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
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class FleePriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new FleePriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            var cu = agent.CombatUnit;

            if (agent.DirectControl.IsUnderDirectControl) {
                return false;
            }

            if (cu.Agent.BeingData.Species.IsWild) {
                return false;
            }

            switch (cu.EffectiveConfig) {
                case CombatUnitConfig.Fight: {
                    return false;
                }
                case CombatUnitConfig.Flee: {
                    // the crew runs from an enemy close by and works otherwise
                    if (cu.Data.IsPlayer && !cu.IsThreatNear()) {
                        return false;
                    }
                    goal.GoalImportance = 2;
                    goal.GoalPriority = 5;
                    return true;
                }
                default: {
                    if (!agent.BeingData.EffectiveFaction.IsPlayer) {
                        // Enemy: flee when severely hurt or army is routing.
                        // These checks apply even to lone units so an injured
                        // solo raider can escape rather than fighting to the death.
                        if (cu.Data.Body.VitalityPercent < Tunables.CombatEnemyFleeHpThreshold) {
                            goal.GoalImportance = 2;
                            goal.GoalPriority = 4;
                            return true;
                        }
                        if (cu.WantsToFleeOnRout
                                && cu.Army.IsRouting()
                                && EscapeRoute.Exists(cu)) {
                            goal.GoalImportance = 3;
                            goal.GoalPriority = 5;
                            return true;
                        }
                    }
                    // Prevent a fresh lone raider from fleeing via the weaker
                    // basePriority path (e.g. "I'm being targeted"). Genuine
                    // injury/rout triggers are already handled above.
                    // IsRouting() already returns false for InitialUnitCount < 2.
                    if (cu.Army.Units.Count < 2) {
                        return false;
                    }
                    break;
                }
            }

            int basePriority = 0;
            int baseImportance = 0;
            var isBio = agent.BeingData.Species.IsBiological;
            int enemiesTargeting = cu.EnemiesTargetingThis;
            var canAttack = cu.CanAttack;
            if (!canAttack && enemiesTargeting > 0) {
                baseImportance += 1;
                basePriority += 3;
            }
            if (isBio) {
                if (cu.Data.Body.IsInjured) {
                    baseImportance += 1;
                    basePriority += 2;
                } else if (cu.Data.Body.IsRecovering) {
                    basePriority += 1;
                }
                basePriority += enemiesTargeting;
            } else {
                if (cu.Data.Body.IsInjured) {
                    basePriority += 1;
                }
                if (enemiesTargeting > 1) {
                    basePriority += enemiesTargeting - 1;
                }
            }
            if (basePriority > 0) {
                if (!agent.BeingData.EffectiveFaction.IsPlayer) {
                    // enemies will be less prone to flee
                    basePriority -= 1;
                    if (basePriority < 1) {
                        return false;
                    }
                }
                if (basePriority > 5) {
                    baseImportance += basePriority / 5;
                    basePriority = 3;
                }
                goal.GoalImportance = (byte) baseImportance;
                goal.GoalPriority = (byte) basePriority;
                return true;
            }
            return false;
        }

        private FleePriorityResolver() : base(PriorityResolverId.Flee) { }
    }
}
