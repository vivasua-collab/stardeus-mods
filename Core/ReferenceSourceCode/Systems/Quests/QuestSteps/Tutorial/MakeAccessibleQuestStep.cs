using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class MakeAccessibleQuestStep : DeviceRoomQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new MakeAccessibleQuestStep(s, c, l));
        }
        public const string StepId = "MakeAccessible";

        public MakeAccessibleQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            if (def == null) { return; }
            RefreshTitle();
            step.Desc = T.QuestStepMakeAccessibleTip;
        }

        protected override string BuildTitle() {
            return T.QuestStepMakeAccessible_V1.F(QuestStepLinks.DeviceLink(S, def));
        }

        protected override bool IsRoomDone(Entity device) {
            var a = S.Sys.Areas;
            var areaId = a.At(device.PosIdx);
            return a.IsAreaReachable(areaId);
        }
    }
}
