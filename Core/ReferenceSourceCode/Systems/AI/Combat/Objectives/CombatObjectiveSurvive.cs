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
    public sealed class CombatObjectiveSurvive : CombatObjective {
        public override string NameT => T.CombatObjectiveSurvive;
        public override string IconId => Constants.IconId.CHeart;

        [SelfInit]
        private static void Register() {
            RegisterObjective(new CombatObjectiveSurvive());
        }

        protected override void SetGoals(CombatUnit unit, long ticks) {
            if (unit.Goals.Count > 0) {
                return;
            }
            // the unit stays unaware if it was before
            AIGoal.CreateCombat(T.CombatStateFindenemy, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasSearchedForEnemy, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.SearchForEnemy);
            AddPositioningGoals(unit, ticks);
            AIGoal.CreateCombat(T.CombatGoalAttackTurret, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasAttackedTurret, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.AttackTurret);
            AddFleeGoalFor(unit, ticks);
        }

        private static void AddFleeGoalFor(CombatUnit unit, long ticks) {
            int fleeVar = unit.Agent.Motion.IsFlying ? AIVarsH.HasEscapedMap : AIVarsH.HasFled;
            AIGoal.CreateCombat(T.VerbFlee, unit, ticks)
                    .WithWorldToDesired(fleeVar, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.Flee);
        }

        public override void Tick(GameState s, CombatUnit unit, long ticks) {
            ReofferGoals(unit, ticks);
        }

        public override bool IsAchieved(GameState s, Army army) {
            return false;
        }

        public CombatObjectiveSurvive() : base(CombatObjectiveId.Survive) {}
    }
}
