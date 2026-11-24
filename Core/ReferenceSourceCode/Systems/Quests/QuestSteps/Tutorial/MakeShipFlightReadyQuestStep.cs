using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class MakeShipFlightReadyQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new MakeShipFlightReadyQuestStep(s, c, l));
        }
        public const string CheckId = "MakeShipFlightReady";
        public MakeShipFlightReadyQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            step.Title = T.QuestStepMakeShipFlightReady;
            var overlay = S.Sys.ShipNav.Overlays[0];
            step.Desc = T.QuestStepMakeShipFlightReadyTip_V1.F(
                Texts.Accented(overlay.IconTooltip));
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            return S.Sys.ShipNav.CheckFlightReady(out _, out _, out _, out _);
        }
    }
}
