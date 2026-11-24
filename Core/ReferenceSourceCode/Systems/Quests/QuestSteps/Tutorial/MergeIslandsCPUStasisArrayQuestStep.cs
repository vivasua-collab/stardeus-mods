using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class MergeIslandsCPUStasisArrayQuestStep : BaseQuestStep, IPreCheckedQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new MergeIslandsCPUStasisArrayQuestStep(s, c, l));
        }
        public const string StepId = "MergeIslandsCPUStasisArray";
        private bool isSubscribed;
        public MergeIslandsCPUStasisArrayQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            step.Title = T.QuestConnecttostasisarrayMergeIslands;
            SetDesc(T.QuestConnecttostasisarrayMergeIslandsTip);
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
                S.Sig.IslandCountChanged.AddListener(OnIslandCountChange);
            }
        }

        private void OnIslandCountChange(int before, int after) {
            if (before <= after) { return; }
            if (CheckCompleted()) {
                step.SetState(QuestStepState.Completed);
            }
        }

        // WARNING: if scenario has more than one ship computer, this won't work well
        public bool CheckCompleted() {
            var cpu = S.Components.FindFirstMatching<ShipComputerComp>(
                c => c.IsConstructed);
            if (cpu == null) { return false; }
            var arrays = S.Components.AllByType<StasisArrayComp>();
            if (arrays == null) { return true; }
            foreach (StasisArrayComp array in arrays) {
                if (!array.IsConstructed) { continue; }
                if (!S.Sys.Areas.IsSameIsland(cpu.Tile.PosIdx, array.Tile.PosIdx)) {
                    return false;
                }
            }
            return true;
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.IslandCountChanged.RemoveListener(OnIslandCountChange);
            }
        }

        public void PreCheck() {
            if (CheckCompleted()) {
                step.SetHidden();
                step.SetState(QuestStepState.Completed);
            }
        }
    }
}
