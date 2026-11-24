using Game.CodeGen;
using Game.Constants;
using Game.Data;
using KL.Utils;

namespace Game.Systems.AI.Combat {
    public abstract class CombatObjective {
        public string Id;
        public int IdH;
        public abstract string NameT { get; }
        public abstract string IconId { get; }
        public abstract void Tick(GameState s, CombatUnit unit, long ticks);
        public abstract bool IsAchieved(GameState s, Army army);
        protected static void RegisterObjective(CombatObjective obj) {
            Registries.CombatObjectives.Add(obj.Id, obj);
        }
        public virtual void OnActivate(GameState s, CombatUnit unit, long ticks) {
            SetGoals(unit, ticks);
        }
        public virtual void OnUnitChanged(CombatUnit unit, long ticks) {
            SetGoals(unit, ticks);
        }
        public virtual void OnDeactivate(GameState s, CombatUnit unit, long ticks) {}
        public virtual void OnNoticeTarget(GameState s, CombatUnit unit, CombatUnit target) {}
        // True for a unit that only fights from where it stands and never goes looking for
        // enemies (CombatObjectiveDefend)
        public virtual bool RespondsInPlace(CombatUnit unit) {
            return false;
        }
        // Fills an empty goal list; a unit that has goals keeps them
        protected abstract void SetGoals(CombatUnit unit, long ticks);

        // Combat goals cannot wait out a failure backoff (CombatSystem.md): every inactive one
        // is offered again on the next objective tick
        protected static void ReofferGoals(CombatUnit unit, long ticks) {
            foreach (var g in unit.Goals) {
                if (!AIGoal.IsActive(g.State)) {
                    g.ClearCounters();
                    g.ChangeStateTo(AIGoalState.Available, ticks);
                    g.ResetWorldState();
                }
            }
        }

        // Humans position themselves: cover, a good firing spot, holding it. Bots
        // (CombatUnit.IsTactical false) have a single goal instead: go at the target and
        // hit it (AICombatActionAdvance).
        protected static void AddPositioningGoals(CombatUnit unit, long ticks) {
            if (!unit.IsTactical) {
                AIGoal.CreateCombat(T.CombatObjectiveAttack, unit, ticks)
                        .WithWorldToDesired(AIVarsH.IsInStrikePosition, false, true)
                        .WithPriorityResolver(PriorityResolverIdH.Advance);
                return;
            }
            AIGoal.CreateCombat(T.CombatGoalFindCover, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsInCover, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.GetInCover);
            AIGoal.CreateCombat(T.CombatGoalFindBestPos, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsInAttackPosition, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.PrepareForAttack);
            AIGoal.CreateCombat(T.CombatGoalHoldPos, unit, ticks)
                    .WithWorldToDesired(AIVarsH.IsHoldingPosition, false, true)
                    .WithPriorityResolver(PriorityResolverIdH.HoldPosition);
        }

        protected CombatObjective(string id) {
            Id = id;
            IdH = Hashes.S(id);
        }
    }
}
