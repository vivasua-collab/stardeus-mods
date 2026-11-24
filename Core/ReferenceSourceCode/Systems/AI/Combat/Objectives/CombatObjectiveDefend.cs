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
    public sealed class CombatObjectiveDefend : CombatObjective {
        [SelfInit]
        private static void Register() {
            RegisterObjective(new CombatObjectiveDefend());
        }
        public override string NameT => T.CombatObjectiveDefend;
        public override string IconId => Constants.IconId.CShield;

        // Respond is how the crew fights on its own (RespondsInPlace). The hunting goals are
        // there for player orders and for the balance tests' switch; their resolvers turn them
        // down while the unit responds in place.
        protected override void SetGoals(CombatUnit unit, long ticks) {
            if (unit.Goals.Count > 0) {
                return;
            }
            AIGoal.CreateCombat(T.CombatObjectiveDefend, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasResponded, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.Respond);
            AIGoal.CreateCombat(T.CombatStateFindenemy, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasSearchedForEnemy, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.SearchForEnemy);
            AddPositioningGoals(unit, ticks);
            AIGoal.CreateCombat(T.CombatStateEscape, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasFled, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.Flee);
        }

        // The crew's response to enemies: it fires at what it can hit from where it stands,
        // melee fighters go at enemies close by, and everyone goes back to work after. Nobody
        // goes looking for enemies or takes cover on their own. A player's order on a target
        // lifts it, and so does the balance tests' switch.
        public override bool RespondsInPlace(CombatUnit unit) {
            return unit.Army?.Faction.IsPlayer == true
                && !unit.HasPlayerOrder
                && !CombatSys.DefendersHuntForBalanceTests;
        }

        public override void OnNoticeTarget(GameState s, CombatUnit unit, CombatUnit target) {
            if (unit.State == CombatUnitState.Unaware) {
                unit.SetState(CombatUnitState.Aggressive);
            }
        }

        public override void Tick(GameState s, CombatUnit unit, long ticks) {
            EnsureRespondGoal(unit, ticks);
            ReofferGoals(unit, ticks);
        }

        // Units saved mid-fight before Respond existed have their goals already
        private static void EnsureRespondGoal(CombatUnit unit, long ticks) {
            if (unit.Goals.Count == 0) { return; }
            for (int i = 0; i < unit.Goals.Count; i++) {
                if (unit.Goals[i].PriorityResolver?.IdH == PriorityResolverIdH.Respond) {
                    return;
                }
            }
            AIGoal.CreateCombat(T.CombatObjectiveDefend, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasResponded, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.Respond);
        }

        public override bool IsAchieved(GameState s, Army army) {
            return false;
        }

        public CombatObjectiveDefend() : base(CombatObjectiveId.Defend) {}
    }
}
