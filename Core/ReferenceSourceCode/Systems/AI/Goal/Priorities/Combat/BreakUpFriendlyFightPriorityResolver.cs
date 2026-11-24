using Game.Constants;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class BreakUpFriendlyFightPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new BreakUpFriendlyFightPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            if (agent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            var cu = agent.CombatUnit;
            int basePriority = 0;
            int baseImportance = 0;
            if (!cu.HasLineOfSight && cu.DistToAttackTarget > 7f) {
                // the agent is being carried away
                basePriority = (int) (cu.DistToAttackTarget) / 5;
                baseImportance = basePriority / 2;
            }
            var numWounds = cu.Data.Body.NumHealableWounds;
            if (numWounds == 0) {
                if (basePriority == 0) {
                    return false;
                }
            } else {
                basePriority += numWounds;
                baseImportance += numWounds / 2;
            }
            if (basePriority > 5) {
                baseImportance += basePriority / 5;
                basePriority = 3;
            }
            goal.GoalImportance = (byte) baseImportance;
            goal.GoalPriority = (byte) basePriority;
            // D.Warn("Priority for goal: {0}/{1}: {2}",
            //     baseImportance, basePriority, goal);
            return true;
        }

        private BreakUpFriendlyFightPriorityResolver() : base(PriorityResolverId.BreakUpFriendlyFight) { }
    }
}
