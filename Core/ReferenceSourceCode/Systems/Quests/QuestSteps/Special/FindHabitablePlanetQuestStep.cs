using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class FindHabitablePlanetQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new FindHabitablePlanetQuestStep(s, c, l));
        }
        public const string CheckId = "FindHabitablePlanet";
        public FindHabitablePlanetQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            step.Title = T.QuestStepFindHabitablePlanet;
            step.Desc = T.QuestStepFindHabitablePlanetTip;
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            return S.Sys.Terraform.KnownTerraformable.Count > 0;
        }
    }
}
