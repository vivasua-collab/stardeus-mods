using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Systems.Conditions;
using Game.Systems.Stats;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.AI {
    public sealed class CombatObjectiveRaid : CombatObjective {
        public override string NameT => T.CombatObjectiveRaid;
        public override string IconId => Constants.IconId.CAttackTarget;

        [SelfInit]
        private static void Register() {
            RegisterObjective(new CombatObjectiveRaid());
        }

        protected override void SetGoals(CombatUnit unit, long ticks) {
            if (unit.Goals.Count > 0) {
                return;
            }
            unit.SetState(CombatUnitState.Aggressive);
            AIGoal.CreateCombat(T.CombatStateFindenemy, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasSearchedForEnemy, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.SearchForEnemy);
            AIGoal.CreateCombat(T.CombatGoalAttackCpu, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasAttackedShipComputer, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.AttackShipComputer);
            AddPositioningGoals(unit, ticks);
            AIGoal.CreateCombat(T.CombatGoalAttackTurret, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasAttackedTurret, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.AttackTurret);
            AddFleeGoalFor(unit, ticks);
        }

        private static void AddFleeGoalFor(CombatUnit unit, long ticks) {
            int fleeVar = EscapeRoute.FleeVarFor(unit);
            AIGoal.CreateCombat(T.CombatStateEscape, unit, ticks)
                    .WithWorldToDesired(fleeVar, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.Flee);
        }

        public override void Tick(GameState s, CombatUnit u, long ticks) {
            var isRouting = u.Army.IsRouting();
            if (u.State == CombatUnitState.Unaware) {
                D.Warn("Raider was unaware, changing to aggressive: {0}", u);
                u.SetState(CombatUnitState.Aggressive);
            }
            foreach (var g in u.Goals) {
                if (!AIGoal.IsActive(g.State)) {
                    g.ClearCounters();
                    g.ChangeStateTo(AIGoalState.Available, ticks);
                    g.ResetWorldState();
                } else if (isRouting
                        && IsFleeGoal(g)
                        && g.State == AIGoalState.Completed) {
                    // Army just started routing — reactivate escape goal for units not yet fleeing
                    g.ClearCounters();
                    g.ChangeStateTo(AIGoalState.Available, ticks);
                    g.ResetWorldState();
                }
            }
        }

        private static bool IsFleeGoal(AIGoal goal) {
            return goal.DesiredState.Get(AIVarsH.HasEscapedMap, false)
                || goal.DesiredState.Get(AIVarsH.HasFled, false);
        }

        public override bool IsAchieved(GameState s, Army army) {
            return false;
        }

        public CombatObjectiveRaid() : base(CombatObjectiveId.Raid) {}
    }
}
