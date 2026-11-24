using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class PrepareForAttackPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new PrepareForAttackPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            // the undrafted crew fights from where it stands (AICombatActionRespond)
            if (agent.CombatUnit.RespondsInPlace) {
                return false;
            }
            var cu = agent.CombatUnit;
            var cfg = cu.EffectiveConfig;
            var canAttack = cu.CanAttack;
            // Only drafted units with an attack order get here (AISys). The order is carried
            // out over the AI's tactics: it beats the suppression pin like a move order does,
            // and the drafted mode of the action holds the unit where it can shoot.
            // canAttack is false while nothing reaches the target, which is why it walks.
            var isOrdered = agent.DirectControl.IsUnderDirectControl
                && cu.BestAttack?.IsMelee == false;
            if (cfg == CombatUnitConfig.Flee) {
                return false;
            }
            if (!canAttack && !isOrdered) {
                return false;
            }

            // Suppressed ranged unit: never reposition — moving means leaving cover
            if (cu.IsSuppressed && cu.BestAttack?.IsMelee == false && !isOrdered) {
                return false;
            }

            // Ranged unit in any cover with its target in sight and in its gun's reach: never
            // reposition. Out of reach it has to close in, or it never fires at all.
            // Unless a hostile has closed inside our own weapon's minimum range - then
            // repositioning (backing off) is exactly what's needed, see below.
            if (cu.BestAttack?.IsMelee == false && !isOrdered
                    && cu.HasCover
                    && cu.RangedTargetInReach
                    && !cu.IsEnemyTooClose) {
                return false;
            }

            // Enemy inside minimum range: urgently back off, at max priority so this beats
            // GetInCover's ordinary priority 4. Skipped while cooling down after repeatedly
            // failing to find anywhere to retreat to (AICombatActionPrepareForAttack corners).
            if (cu.IsEnemyTooClose
                    && !agent.DirectControl.IsUnderDirectControl
                    && !agent.BB.IsCoolingDown(AICombatActionPrepareForAttack.BackOffCooldownIdH, ticks)) {
                goal.GoalImportance = 3;
                goal.GoalPriority = 5;
                return true;
            }

            if (cfg == CombatUnitConfig.Fight) {
                goal.GoalImportance = 2;
            } else {
                goal.GoalImportance = 1;
            }
            if (cu.AttackTarget == null) {
                goal.GoalImportance = 1;
                goal.GoalPriority = 3;
                return false;
            }
            if (cu.BestAttack == null) {
                goal.GoalImportance = 1;
                goal.GoalPriority = 1;
                return false;
            }
            if (cu.BestAttack.IsMelee && !cu.MeleeInAttackRange) {
                goal.GoalPriority = 4;
                return true;
            }
            if (cu.HasGoodCover && cu.HasLineOfSight) {
                goal.GoalPriority = 2;
            } else {
                goal.GoalPriority = 3;
            }
            return true;
        }

        private PrepareForAttackPriorityResolver() : base(PriorityResolverId.PrepareForAttack) { }
    }
}
