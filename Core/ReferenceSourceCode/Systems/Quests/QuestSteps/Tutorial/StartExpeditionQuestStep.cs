using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class StartExpeditionQuestStep : BaseQuestStep, ITickListener,
            IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new StartExpeditionQuestStep(s, c, l));
        }
        public const string CheckId = "StartExpedition";
        public StartExpeditionQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            // This is stupid, but the reason is that we may want to have
            // this step with custom text, and in that case we don't want to
            // overwrite the title. Default title is step.Def.id, so if it's
            // created like that, we will overwrite.
            // TODO: figure out why setting this when !isLoading didn't work.
            if (step.Title == step.Def.Id) {
                step.Title = T.QuestStepStartExpedition;
                step.Desc = T.QuestStepStartExpeditionDesc_V1.F(
                    Texts.LinkToCodex(DefIdsH.ObjectsDevicesShuttle));
            }
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            var exps = S.Sys.Expedition.Expeditions;
            for (int i = 0; i < exps.Count; i++) {
                if (exps[i].Shuttle?.IsOut == true) { return true; }
            }
            return false;
        }

        public void PreCheck() {
            if (S.Situation.ExpeditionsOngoing > 0 || S.Situation.ExpeditionsCompleted > 0) {
                step.Data.SetState(QuestState.Deleted);
            }
        }
    }
}
