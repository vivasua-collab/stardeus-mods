using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class HaveOneSectionQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HaveOneSectionQuestStep(s, c, l));
        }
        public const string CheckId = "HaveOneSection";
        public HaveOneSectionQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            step.Title = T.QuestStepHaveOneSection;
            step.Desc = T.QuestStepHaveOneSectionTip;
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            return S.Sys.Areas.IslandCount == 1;
        }
    }
}
