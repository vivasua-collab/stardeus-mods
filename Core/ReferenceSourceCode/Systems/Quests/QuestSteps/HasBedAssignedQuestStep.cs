using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Sleep;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Completes when the giver colonist has a permanent (non-temporary) assigned bed.
    /// Reads the colonist from step.Data.Provider (must be a ColonistQuestProvider).
    /// </summary>
    public sealed class HasBedAssignedQuestStep : BaseQuestStep,
            ITickListener, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new HasBedAssignedQuestStep(s, c, l));
        }

        public const string CheckId = "HasBedAssigned";

        private readonly BeingData colonist;
        private bool isSubscribed;

        public HasBedAssignedQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            if (step.Data.Provider is ColonistQuestProvider cp) {
                colonist = cp.BeingData;
            }
            if (colonist == null) {
                D.Throw("HasBedAssigned: quest provider is not a ColonistQuestProvider | {0}", step);
            }

            var key = step.Def.GetTitleKey(step.Data);
            if (key != null) {
                step.Title = key.T(colonist.Mind.LinkToShortName);
            } else {
                step.Title = T.QuestStepHasBedAssigned_V1.F(colonist.Mind.LinkToShortName);
            }
            step.Desc = T.QuestStepHasBedAssignedDesc;

            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public void Tick(long ticks) {
            if (step.IsActive && CheckCompleted()) {
                CompleteStep();
            }
        }

        public bool CheckCompleted() {
            if (colonist == null || colonist.Being == null || !colonist.Being.IsAlive) {
                return false;
            }
            var bed = S.Query.FindBed.Ask(colonist, false);
            if (bed == null) { return false; }
            var slot = bed.SlotFor(colonist);
            if (slot == null || slot.IsTemporary) { return false; }
            return true;
        }

        private void OnBedSlotStateChanged(uint beingDataId) {
            if (beingDataId != colonist.Id) { return; }
            if (step.IsActive) {
                CheckCompleted();
            }
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.BedSlotStateChanged.AddListener(OnBedSlotStateChanged);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Subscribe();
            }
        }

        public override void Cleanup() {
            if (!isSubscribed) { return; }
            isSubscribed = false;
            S.Sig?.BedSlotStateChanged.RemoveListener(OnBedSlotStateChanged);
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                CompleteStep();
            }
        }
    }
}
