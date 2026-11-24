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
using Game.Systems.Turrets;

namespace Game.Systems.AI {
    public sealed class AttackTurretPriorityResolver : GoalPriorityResolver {
        [SelfInit]
        private static void Register() {
            RegisterResolver(new AttackTurretPriorityResolver());
        }

        public override bool UpdatePriorityFor(AIGoal goal, AIAgentComp agent, long ticks) {
            if (agent.DirectControl.IsUnderDirectControl) {
                return false;
            }
            var cu = agent.CombatUnit;
            var canAttack = cu.CanAttack;
            if (!canAttack) {
                return false;
            }
            if (cu.TurretsTargetingThisUnit.Count > 0) {
                goal.GoalPriority = 4;
                goal.GoalImportance = 2;
                goal.WithInterrupt(3, 0);
                // D.Warn("Turrets targeting this unit: {0}", cu.TurretsTargetingThisUnit.Count);
            } else {
                goal.WithInterrupt(2, 0);
                // two non-capturing lambdas rather than one reading the window, which would
                // allocate a closure on every priority update of every attacker
                var matches = AICombatActionAttackTurret.MayAttackShipWeapons(cu, ticks)
                    ? AnyScanning
                    : ScanningAtBeings;
                if (agent.S.Components.CountByInterfaceMatching(matches, 1) != 0) {
                    // D.Warn("Has attackable turrets");
                    goal.GoalImportance = 1;
                    goal.GoalPriority = 4;
                } else {
                    // D.Warn("No turrets");
                    // no turrets
                    goal.GoalPriority = 0;
                    return false;
                }
            }
            return true;
        }

        private static readonly Func<IAttackableTurret, bool> AnyScanning = t => t.IsScanning;
        private static readonly Func<IAttackableTurret, bool> ScanningAtBeings =
            t => t.IsScanning && t.ShootsBeings;

        private AttackTurretPriorityResolver() : base(PriorityResolverId.AttackTurret) { }
    }
}
