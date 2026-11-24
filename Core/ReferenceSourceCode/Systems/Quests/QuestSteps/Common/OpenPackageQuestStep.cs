using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.Quests {
    public sealed class OpenPackageQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new OpenPackageQuestStep(s, c, l));
        }
        public const string CheckId = "OpenPackage";
        private string packageStepId;
        private bool isSubscribed = false;
        public OpenPackageQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            packageStepId = step.Def.Config.GetString(PropertyIdH.PackageStepId, null);
            step.Title = T.QuestOpenPackage;
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState _, QuestStepState after) {
            if (after == QuestStepState.Active) {
                Subscribe();
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.QuestPackageStateChanged.RemoveListener(OnPackStateChange);
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.QuestPackageStateChanged.AddListener(OnPackStateChange);
            }
        }

        private void OnPackStateChange(QuestPackage pack, QuestPackageState before,
                QuestPackageState after) {
            if (pack.Step.Data != step.Data) {
                // not our quest
                return;
            }
            if (packageStepId != null && packageStepId != pack.Step?.Def.Id) {
                // not our package
                return;
            }
            if (after == QuestPackageState.Destroyed) {
                FailStep();
            } else if (after == QuestPackageState.Opened) {
                CompleteStep();
            }
        }
    }
}
