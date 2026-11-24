using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class EnableElysiumQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new EnableElysiumQuestStep(s, c, l));
        }
        public const string StepId = "EnableElysium";
        private bool isSubscribed;
        public EnableElysiumQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            step.Title = T.QuestStepEnableElysiumTitle;
            var stasisDef = The.Defs.FastGet(DefIdsH.ObjectsDevicesStasisArray);
            step.Desc = T.QuestStepEnableElysiumDesc_V2.F(stasisDef.CodexLink,
                Texts.Accented(T.StasisElysium));
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (CheckEnabled()) {
                    CompleteStep();
                    return;
                }
                Subscribe();
            }
        }

        private bool CheckEnabled() {
            return S.Components.CountMatching<StasisArrayComp>(
                c => c.IsElysium, 1) > 0;
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.ElysiumEnabled.AddListener(OnEnableElysium);
            }
        }

        private void OnEnableElysium() {
            CompleteStep();
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.ElysiumEnabled.RemoveListener(OnEnableElysium);
            }
        }
    }
}
