using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class UseHyperjumpRelayQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new UseHyperjumpRelayQuestStep(s, c, l));
        }
        public const string CheckId = "UseHyperjumpRelay";
        public UseHyperjumpRelayQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            step.Title = T.QuestStepUseHyperjumpRelay;
            step.Desc = T.QuestStepUseHyperjumpRelayTip_V2.F(
                Texts.Accented(T.SpaceObjectHyperjumprelay), Texts.Accented(T.VerbHyperjump));
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            return S.Universe.IsUsingHyperspaceRelay;
        }
    }
}
