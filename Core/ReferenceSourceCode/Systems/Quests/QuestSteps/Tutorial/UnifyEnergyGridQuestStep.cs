using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class UnifyEnergyGridQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(StepId,
                (s, c, l) => new UnifyEnergyGridQuestStep(s, c, l));
        }
        public const string StepId = "UnifyEnergyGrid";
        private bool isSubscribed;
        public UnifyEnergyGridQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(StepId, s, step, isLoading) {
            var title = T.QuestConnecttosurvivorareaFloorSocket;
            // replace <b>Floor Socket</b> with the actual socket in the world, if it exists
            var name = T.ObjectsDevicesFloorsocket_V2.Replace("{0}-{1}", string.Empty).Trim();
            var socket = s.Components.FindFirstMatching<ElectricNodeComp>(
                n => n.Tile.Definition.IdH == DefIdsH.ObjectsDevicesFloorSocket
                    && n.IsConstructed && n.IsConnected && !n.IsReachable);
            string repl;
            if (socket != null) {
                repl = Texts.LinkToTile(socket.Tile);
            } else {
                repl = Texts.LinkToCodex(DefIdsH.ObjectsDevicesFloorSocket);
            }
            step.Title = title.Replace(name, repl);
            SetDesc(T.QuestConnecttosurvivorareaFloorSocketTip.Replace(name, repl));
            if (isLoading && step.IsActive) {
                Subscribe();
            }
        }

        public override void OnStateChange(QuestStepState before, QuestStepState after) {
            if (after == QuestStepState.Active) {
                if (A.Grid.GridsCount > 1) {
                    Subscribe();
                } else {
                    step.SetState(QuestStepState.Completed);
                    // if player prematurely completed this step, we still want to show this
                    A.S.UIQuery.ShowTutorialPopup.Ask(UnifyingGridPage.PageId, 10f);
                }
            }
        }

        private void Subscribe() {
            if (!isSubscribed) {
                isSubscribed = true;
                S.Sig.ElectricityGridChanged.AddListener(OnGridChanged);
                A.S.UIQuery.ShowTutorialPopup.Ask(UnifyingGridPage.PageId, 10f);
            }
        }

        private void OnGridChanged() {
            if (A.Grid.GridsCount == 1) {
                step.SetState(QuestStepState.Completed);
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.ElectricityGridChanged.RemoveListener(OnGridChanged);
            }
        }

    }
}
