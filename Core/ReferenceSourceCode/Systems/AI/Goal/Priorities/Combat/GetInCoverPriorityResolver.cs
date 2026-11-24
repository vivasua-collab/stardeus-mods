using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.AI {
    public sealed class GetInCoverPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new GetInCoverPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            // the undrafted crew fights from where it stands (AICombatActionRespond)
            if (agent.CombatUnit.RespondsInPlace) {
                return false;
            }
            if (agent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            var cu = agent.CombatUnit;
            if (cu.State == CombatUnitState.Unaware) {
                return false;
            }
            if (agent.BB.IsCoolingDown(AICombatActionGetInCover.ActIdH, ticks)) {
                return false;
            }
            if (cu.EffectiveConfig == CombatUnitConfig.Fight) {
                goal.GoalImportance = 2;
            } else {
                goal.GoalImportance = 1;
            }

            if (!cu.Agent.Motion.IsBusy && agent.S.Sys.Path.IsPassThroughOnly(agent.PosIdx)) {
                goal.GoalImportance = 3;
                goal.GoalPriority = 5;
                return true;
            }

            // Only ranged units dance around looking for a safer spot.
            // Melee brawlers push straight into combat heat. Units with no usable
            // attack (BestAttack == null) still hide - cover is all they have.
            if (cu.BestAttack?.IsMelee == true) {
                return false;
            }

            // Suppressed unit with no cover: urgently seek cover regardless
            if (cu.IsSuppressed && !cu.HasCover) {
                goal.GoalImportance = 3;
                goal.GoalPriority = 5;
                return true;
            }

            // Has a target but no LOS: let PrepareForAttack drive the approach instead
            if (cu.AttackTarget != null && !cu.HasLineOfSight) {
                return false;
            }

            // Ranged unit in any cover with a shootable target: suppress cover-upgrade urge
            if (cu.BestAttack?.IsMelee == false
                    && cu.HasCover
                    && cu.RangedTargetInReach) {
                return false;
            }

            if (cu.HasGoodCover) {
                goal.GoalPriority = 3;
                return false;
            }
            goal.GoalPriority = 4;
            return true;
        }

        private GetInCoverPriorityResolver() : base(PriorityResolverId.GetInCover) { }
    }
}
