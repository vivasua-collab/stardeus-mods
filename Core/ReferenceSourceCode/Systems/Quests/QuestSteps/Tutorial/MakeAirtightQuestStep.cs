using Game.Constants;
using Game.Data;
using Game.Systems.Atmo;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class MakeAirtightQuestStep : DeviceRoomQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new MakeAirtightQuestStep(s, c, l));
        }
        public const string StepId = "MakeAirtight";

        public MakeAirtightQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            if (def == null) { return; }
            RefreshTitle();
            step.Desc = Texts.LinkToCodexManual(T.LearnHowToAirtight, PressurizingAreasPage.PageId);
        }

        protected override string BuildTitle() {
            return T.QuestStepMakeAirtight_V1.F(QuestStepLinks.DeviceLink(S, def));
        }

        protected override void OnActivated() {
            A.S.UIQuery.ShowTutorialPopup.Ask(PressurizingAreasPage.PageId, 12f);
        }

        protected override bool IsRoomDone(Entity device) {
            var areaId = S.Sys.Areas.At(device.PosIdx);
            return AreaSealing.IsAirtight(S, areaId);
        }
    }
}
