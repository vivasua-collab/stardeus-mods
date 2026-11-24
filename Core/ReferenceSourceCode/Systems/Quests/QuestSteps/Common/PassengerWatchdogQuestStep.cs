using Game.CodeGen;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Hidden watchdog that spans a passenger delivery quest. Fails the quest if the passenger
    /// dies (BeingDied) or defects to their own faction's raid (BeingDefected, raised by
    /// CombatSys). On quest end it routes the passenger through the single idempotent cleanup
    /// so a still-aboard guest is removed and its bed freed.
    /// </summary>
    public sealed class PassengerWatchdogQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new PassengerWatchdogQuestStep(s, c, l));
        }

        public const string CheckId = "PassengerWatchdog";

        private bool isSubscribed;

        public PassengerWatchdogQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Subscribe();
            }
        }

        public override void OnQuestStateChange(QuestState before, QuestState after) {
            if ((after & QuestState.Ended) != 0) {
                // Quest is over: if the passenger is still a guest aboard (e.g. failed by
                // deadline), remove them. Idempotent: no-op after delivery or defection.
                var data = FindPassenger();
                if (data != null) {
                    PassengerUtils.EndGuestRole(S, data, despawn: true);
                }
            }
        }

        private BeingData FindPassenger() {
            var mindId = PassengerMindId();
            if (mindId == 0) { return null; }
            return S.Query.FindMind.Ask(mindId)?.Owner;
        }

        private uint PassengerMindId() {
            var d = step.Data.Data;
            if (d == null) { return 0; }
            return (uint) d.GetInt(PassengerUtils.PassengerMindIdKey, 0);
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.BeingDied.AddListener(OnPassengerLost);
            S.Sig.BeingDefected.AddListener(OnPassengerLost);
        }

        private void OnPassengerLost(BeingData being) {
            if (!step.IsActive) { return; }
            var mindId = PassengerMindId();
            if (mindId != 0 && being.Mind.Id == mindId) {
                FailStep();
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.BeingDied.RemoveListener(OnPassengerLost);
                S.Sig?.BeingDefected.RemoveListener(OnPassengerLost);
            }
        }
    }
}
