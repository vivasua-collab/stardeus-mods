using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;

namespace Game.Systems.Quests {
    // Enclosed means what the Rooms overlay and the lighting show (the 4-connected room flood
    // fill). Walls that meet only at a corner still enclose the room; the gap they leave is the
    // next step's lesson (CloseCornerGapsQuestStep), see AreaSealing for why.
    public sealed class EncloseEntityQuestStep : DeviceRoomQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new EncloseEntityQuestStep(s, c, l));
        }
        public const string StepId = "EncloseEntity";

        public EncloseEntityQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            if (def == null) { return; }
            RefreshTitle();
            step.Desc = T.QuestStepEncloseTip_V1.F(def.CodexLink);
        }

        protected override string BuildTitle() {
            return T.QuestStepEnclose_V1.F(QuestStepLinks.DeviceLink(S, def));
        }

        protected override void OnActivated() {
            A.S.UIQuery.ShowTutorialPopup.Ask(EnclosingAreasPage.PageId, 10f);
        }

        protected override bool IsRoomDone(Entity device) {
            return S.Sys.Areas.IsEnclosed(device.PosIdx);
        }
    }
}
