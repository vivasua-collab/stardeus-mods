using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class HoldPositionPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new HoldPositionPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            // the undrafted crew fights from where it stands (AICombatActionRespond)
            if (agent.CombatUnit.RespondsInPlace) {
                return false;
            }
            if (!agent.Motion.IsAtClaimedPos) {
                return false;
            }
            var cu = agent.CombatUnit;

            if (agent.S.Sys.Path.IsPassThroughOnly(cu.PosIdx)) {
                return false;
            }

            // A drafted shooter has an attack order (AISys lets no other drafted unit pick
            // goals): PrepareForAttack carries it out and holds it wherever it can shoot.
            // Holding here could keep it behind cover, out of sight of its target.
            if (agent.DirectControl.IsUnderDirectControl && cu.BestAttack?.IsMelee == false) {
                return false;
            }

            // Suppressed unit: force maximum hold — never leave cover while suppressed
            if (cu.IsSuppressed) {
                goal.GoalImportance = 3;
                goal.GoalPriority = 5;
                return true;
            }

            // Melee unit within strike range: lock in place at max priority
            if (cu.MeleeInAttackRange) {
                if (cu.EffectiveConfig == CombatUnitConfig.Fight) {
                    goal.GoalImportance = 3;
                } else {
                    goal.GoalImportance = 2;
                }
                goal.GoalPriority = 5;
                return true;
            }

            // Melee unit not in strike range: don't hold — move in to close the gap
            if (cu.BestAttack?.IsMelee == true) {
                return false;
            }

            // --- Ranged unit logic below ---
            var canAttack = cu.CanAttack;
            int enemiesTargeting = cu.EnemiesTargetingThis;
            if (!canAttack && enemiesTargeting > 0) {
                return false;
            }
            if (cu.AttackTarget != null) {
                // holding a spot the gun can't shoot from only waits out the fight
                if (!cu.HasGoodCover || !cu.RangedTargetInReach) {
                    return false;
                }
            } else {
                if (!cu.HasLineOfSight) {
                    return false;
                }
            }

            if (cu.IsInBadPos) {
                return false;
            }
            if (cu.TurretsTargetingThisUnit.Count > 0) {
                return false;
            }

            if (enemiesTargeting > 0) {
                float gtfoRange = cu.ClosestRange;
                if (gtfoRange > 0f) {
                    foreach (var e in cu.EnemiesTargetingThisUnit) {
                        if (Vectors.SqrDistance(e.Position, cu.Position) < gtfoRange * gtfoRange) {
                            return false;
                        }
                    }
                }
            }

            if (cu.EffectiveConfig == CombatUnitConfig.Fight) {
                goal.GoalImportance = 2;
            } else {
                goal.GoalImportance = 1;
            }

            var time = agent.Motion.TicksInCurrentPosition;
            if (time > Consts.TicksPer10Minutes) {
                return false;
            } else if (time < Consts.TicksPer3Minutes) {
                goal.GoalPriority = 5;
            } else if (time < Consts.TicksPer5Minutes) {
                goal.GoalPriority = 4;
            } else {
                goal.GoalPriority = 3;
            }
            return true;
        }

        private HoldPositionPriorityResolver() : base(PriorityResolverId.HoldPosition) { }
    }
}
