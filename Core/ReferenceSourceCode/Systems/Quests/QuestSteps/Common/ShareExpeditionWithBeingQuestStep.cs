using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Quests;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    /// <summary>
    /// Completes when an expedition enters the Takeoff stage with both the quest giver
    /// and all beings listed in ColonistQuestProvider.ExtraBeingIds aboard the shuttle.
    /// Fails if any required being dies or becomes incapacitated before takeoff.
    /// </summary>
    public sealed class ShareExpeditionWithBeingQuestStep : BaseQuestStep, ITickListener {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ShareExpeditionWithBeingQuestStep(s, c, l));
        }

        public const string CheckId = "ShareExpeditionWithBeing";
        public const string SuppressExpeditionSocialEventsKey =
            "SuppressSocialEvents";

        public override bool SaveLoadTexts => true;

        private uint[] extraBeingIds;
        private bool isSubscribed;

        public ShareExpeditionWithBeingQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            ReadFromProvider();
            if (!isLoading) {
                // Provider may not have ExtraBeingIds set yet at construction time
                // (test path). Defer reading to activation — see ReadFromProvider().
                var cp = step.Data.Provider as ColonistQuestProvider;
                SetTexts(cp);
            }

            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        private void ReadFromProvider() {
            var cp = step.Data.Provider as ColonistQuestProvider;
            extraBeingIds = cp?.ExtraBeingIds;
        }

        private void SetTexts(ColonistQuestProvider cp) {
            if (cp == null || extraBeingIds == null || extraBeingIds.Length == 0) { return; }
            var firstExtra = S.Query.FindMind.Ask(extraBeingIds[0]);
            if (firstExtra == null) { return; }
            step.Title = T.QuestStepSharedExpeditionTitle_V2.F(
                cp.BeingData.Mind.LinkToShortName, firstExtra.LinkToShortName);
            step.Desc = T.QuestStepSharedExpeditionTwoDesc_V2.F(
                cp.BeingData.Mind.LinkToShortName, firstExtra.LinkToShortName);
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                ReadFromProvider();
                if (!CheckExtraBeingsAlive()) {
                    FailStep();
                    return;
                }
                Subscribe();
            }
        }

        private bool CheckExtraBeingsAlive() {
            if (extraBeingIds == null) { return true; }
            for (int i = extraBeingIds.Length - 1; i >= 0; i--) {
                var mind = S.Query.FindMind.Ask(extraBeingIds[i]);
                var being = mind?.Owner;
                if (being == null || being.Being == null || !being.Being.IsAlive) { return false; }
            }
            return true;
        }

        private void Subscribe() {
            if (isSubscribed) { return; }
            isSubscribed = true;
            S.Sig.BeingDied.AddListener(OnBeingDied);
        }

        private void OnBeingDied(BeingData died) {
            if (!step.IsActive) { return; }
            if (extraBeingIds == null) { return; }
            for (int i = extraBeingIds.Length - 1; i >= 0; i--) {
                if (died.Mind.Id == extraBeingIds[i]) {
                    FailStep();
                    return;
                }
            }
        }

        public void Tick(long ticks) {
            if (!step.IsActive) { return; }

            if (extraBeingIds != null) {
                for (int i = extraBeingIds.Length - 1; i >= 0; i--) {
                    var mind = S.Query.FindMind.Ask(extraBeingIds[i]);
                    var being = mind?.Owner;
                    if (being == null || !being.HasStateFlag(BeingState.Alive)) {
                        FailStep();
                        return;
                    }
                }
            }

            var cp = step.Data.Provider as ColonistQuestProvider;
            if (cp == null) { return; }
            var giverData = cp.BeingData;

            var exps = S.Sys.Expedition.Expeditions;
            for (int i = exps.Count - 1; i >= 0; i--) {
                var exp = exps[i];
                if (exp.StageType != ExpeditionStageTypeH.Takeoff) { continue; }
                if (CrewContainsRequired(exp, giverData)) {
                    if (step.Data.Def.IdH == QuestIdH.Random_Personal_ColonistCrushExpedition) {
                        exp.StageData.SetBool(SuppressExpeditionSocialEventsKey,
                            step.SeededRng.Chance(0.9f));
                    }
                    CompleteStep();
                    return;
                }
            }
        }

        private bool CrewContainsRequired(Expedition exp, BeingData giverData) {
            bool hasGiver = false;
            int extrasFound = 0;
            int extrasNeeded = extraBeingIds?.Length ?? 0;

            for (int j = exp.Crew.Count - 1; j >= 0; j--) {
                var member = exp.Crew[j];
                if (member.Id == giverData.Id) {
                    hasGiver = true;
                } else if (extrasNeeded > 0) {
                    for (int k = extrasNeeded - 1; k >= 0; k--) {
                        if (member.Mind.Id == extraBeingIds[k]) {
                            extrasFound++;
                            break;
                        }
                    }
                }
            }
            return hasGiver && extrasFound == extrasNeeded;
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.BeingDied.RemoveListener(OnBeingDied);
            }
        }
    }
}
