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
    public sealed class CombatObjectiveFistFight : CombatObjective {
        [SelfInit]
        private static void Register() {
            RegisterObjective(new CombatObjectiveFistFight());
        }
        public override string NameT => T.VerbFight;
        public override string IconId => Constants.IconId.CFist;

        protected override void SetGoals(CombatUnit unit, long ticks) {
            if (unit.Goals.Count > 0) {
                return;
            }
            AIGoal.CreateCombat(T.CombatStateFindenemy, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasSearchedForEnemy, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.SearchForEnemy);
            AIGoal.CreateCombat(T.CombatGoalFindCover, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsInCover, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.GetInCover);
            AIGoal.CreateCombat(T.CombatGoalFindBestPos, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsInAttackPosition, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.PrepareForAttack);
            AIGoal.CreateCombat(T.CombatGoalHoldPos, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsHoldingPosition, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.HoldPosition);
            AIGoal.CreateCombat(T.CombatStateEscape, unit, ticks)
                    .WithWorldToDesired(AIVarsH.HasFled, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.Flee);
            AIGoal.CreateCombat(T.Stop, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsFriendlyFightBrokenUp, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.BreakUpFriendlyFight);
        }

        public override void OnNoticeTarget(GameState s, CombatUnit unit, CombatUnit target) {
        }

        public override void Tick(GameState s, CombatUnit unit, long ticks) {
            if (TryBreakUpStalemate(unit, ticks)) { return; }
            ReofferGoals(unit, ticks);
        }

        /// <summary>
        /// When both fighters have been continuously unable to attack from wherever they
        /// stand (e.g. separated by an impassable door), the fight can never resolve on its
        /// own and the fighters starve instead. Disbanding our own army ends it; CombatSys
        /// .OnTick cascade cleans up the other side.
        /// </summary>
        private static bool TryBreakUpStalemate(CombatUnit u, long ticks) {
            var stallTimeoutTicks = (long) (Tunables.FriendlyFightStallTimeoutHours * Consts.TicksPerHour);
            var foe = u.AttackTarget;
            if (foe?.Army == null) { return false; }
            if (u.NoAttackStallTicks(ticks) > stallTimeoutTicks
                    && foe.NoAttackStallTicks(ticks) > stallTimeoutTicks) {
                D.Warn("Breaking up friendly fight: {0} and {1} could not reach each other for {2}",
                    u, foe, Units.TicksToTime(stallTimeoutTicks));
                u.Army.Disband();
                return true;
            }
            return false;
        }

        public override bool IsAchieved(GameState s, Army army) {
            return false;
        }

        public CombatObjectiveFistFight() : base(CombatObjectiveId.FistFight) {}
    }
}
