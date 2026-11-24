// not used since translations are whack
#if false
using System;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class SetShipForwardDirQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new SetShipForwardDirQuestStep(s, c, l));
        }
        public const string CheckId = "SetShipForwardDir";
        private bool isSubscribed;
        public SetShipForwardDirQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            step.Title = T.QuestStepSetShipForwardDirection;
            var ov = S.Sys.ShipNav.Overlays[0];
            step.Desc = T.QuestStepSetShipForwardDirectionTip_V1.F(
                Texts.Accented(ov.IconTooltip));
            if (!isSubscribed && step.IsActive) {
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
                S.Sig.ShipForwardDirChanged.AddListener(OnDirChange);
            }
        }

        private void OnDirChange(Facing.Type _, Facing.Type _1) {
            CompleteStep();
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.ShipForwardDirChanged.RemoveListener(OnDirChange);
            }
        }
    }

}
#endif
