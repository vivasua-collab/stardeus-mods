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
    public sealed class AttackShipComputerPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new AttackShipComputerPriorityResolver());
            IsForbidden = false;
        }

        /// <summary>
        /// For tests
        /// </summary>
        public static bool IsForbidden;

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            if (agent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            if (IsForbidden) {
                #if !PRODUCTION_BUILD
                if (!The.TestsAreRunning) {
                    D.Err("Attack Ship Computer is forbidden!");
                }
                #endif
                return false;
            }
            var cu = agent.CombatUnit;
            var canAttack = cu.CanAttack;
            if (!canAttack) {
                return false;
            }
            if (cu.AttackTarget != null) {
                goal.GoalPriority = 2; // lower prio
                return false;
            }
            if (cu.TurretsTargetingThisUnit.Count > 0) {
                return false;
            }
            goal.GoalPriority = 3;
            return true;
        }

        private AttackShipComputerPriorityResolver() : base(PriorityResolverId.AttackShipComputer) { }
    }
}
