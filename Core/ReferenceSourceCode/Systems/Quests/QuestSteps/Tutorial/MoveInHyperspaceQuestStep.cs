using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.Quests {
    public sealed class MoveInHyperspaceQuestStep : BaseQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new MoveInHyperspaceQuestStep(s, c, l));
        }
        public const string CheckId = "MoveInHyperspace";
        private readonly string layer;
        private bool isSubscribed;
        public MoveInHyperspaceQuestStep(GameState s, QuestStep step, bool isLoading)
                : base(CheckId, s, step, isLoading) {
            layer = config.GetString(PropertyIdH.StarmapLayer);
            step.Title = T.QuestStepMoveInHyperspace;
            string desc;
            switch (layer) {
                case UniverseLayer.Universe: {
                    desc = T.QuestStepMoveInHyperspaceDesc_V2.F(
                        Texts.Accented(T.SpaceObjectUniverse),
                        Texts.Accented(T.SpaceObjectRegion));
                    break;
                }
                default: {
                    desc = T.QuestStepMoveInHyperspaceDesc_V2.F(
                        Texts.Accented(T.SpaceObjectRegion),
                        Texts.Accented(T.SpaceObjectSector));
                    break;
                }
            }
            SetDesc(desc);
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
                S.Sig.HyperspaceLocationChanged.AddListener(OnHyperspaceJump);
                A.S.UIQuery.ShowTutorialPopup.Ask(HyperjumpPage.PageId, 3f);
            }
        }

        public override void Cleanup() {
            if (isSubscribed) {
                isSubscribed = false;
                S.Sig?.HyperspaceLocationChanged.RemoveListener(OnHyperspaceJump);
            }
        }

        private void OnHyperspaceJump(SpaceObject from, SpaceObject to) {
            if (layer == UniverseLayer.Universe) {
                if (from.Parent == to.Parent) {
                    // didn't change region
                    return;
                }
            }
            CompleteStep();
        }

    }
}
