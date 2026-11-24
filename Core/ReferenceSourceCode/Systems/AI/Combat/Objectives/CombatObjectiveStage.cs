using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.AI.Combat;
using Game.Utils;

namespace Game.Systems.AI {
    // A staged raid: the raiders wait where they arrived, giving the crew time to
    // prepare, then attack together. Without it raiders arrive strung out by their speed and
    // are picked off one at a time. A gathering unit has no combat goals, so it stays put; it
    // still shoots what comes in reach. The whole army attacks (Army.EndStaging, which hands
    // every unit to CombatObjectiveRaid) when the time is up, when any of its units is harmed
    // (CombatUnit.OnHarmed) or when the crew engages it
    // (CombatSys.OnStartCombatBetween). Until then the army starts no hostile combat
    // (CombatSys.OnStartRaid): no alert, no clock limit while it only waits. Its own
    // objective, not a branch of the raid objective, so the raid logic stays as it is.
    public sealed class CombatObjectiveStage : CombatObjective {
        public override string NameT => T.CombatObjectiveStage;
        public override string IconId => Constants.IconId.CTime;

        [SelfInit]
        private static void Register() {
            RegisterObjective(new CombatObjectiveStage());
        }

        protected override void SetGoals(CombatUnit unit, long ticks) { }

        public override void Tick(GameState s, CombatUnit u, long ticks) {
            var army = u.Army;
            if (army == null) { return; }
            if (!army.IsStaging || ticks >= army.StageUntilTick) {
                army.EndStaging(s, ticks);
            }
        }

        public override bool IsAchieved(GameState s, Army army) {
            return false;
        }

        private CombatObjectiveStage() : base(CombatObjectiveId.Stage) { }
    }
}
