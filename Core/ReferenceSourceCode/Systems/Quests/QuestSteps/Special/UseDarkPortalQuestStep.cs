using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class UseDarkPortalQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new UseDarkPortalQuestStep(s, c, l));
        }
        public const string StepId = "UseDarkPortal";
        private bool isSubscribed;

        public UseDarkPortalQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            step.Title = T.QuestStepUseDarkPortal;
            SetDesc(T.QuestStepUseHyperjumpRelayTip_V2.F(
                T.SpaceObjectDarkportal, T.GoThrough));
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Subscribe();
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.UniverseRegenerated.AddListener(OnUniverseRegenerated);
            }
        }

        private void OnUniverseRegenerated() {
            step.SetState(QuestStepState.Completed);
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.UniverseRegenerated.RemoveListener(OnUniverseRegenerated);
            }
        }
    }
}
