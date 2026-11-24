using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Procgen;
using Game.Systems.UIUX.Codex.Pages;
using Game.Utils;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Quests {
    public sealed class ConfigureMatterReactorQuestStep : PowerTutorialQuestStep {
        [SelfInit]
        private static void Register() {
            Registries.QuestSteps.Add(CheckId,
                (s, c, l) => new ConfigureMatterReactorQuestStep(s, c, l));
        }
        public const string CheckId = "ConfigureMatterReactor";
        public ConfigureMatterReactorQuestStep(GameState s, QuestStep check, bool isLoading)
                : base(CheckId, s, check, isLoading) {
            check.Title = T.QuestProvidechargestationConfigureReactor;
            check.Desc = T.QuestProvidechargestationConfigureReactorTip_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesMatterReactorMini),
                Texts.LinkToCodex(DefIdsH.ObjMaterialsScrapMetal));
            Init(isLoading);
        }

        // The connected reactor may already be configured (e.g. the survivor area's
        // emergency reactor), in which case no unconfigured -> configured signal will ever come.
        // The same goes for a reactor configured first and connected later, which is why the
        // grid changes the base class listens to re-run this too.
        // The tutorial popup is queued in OnSubscribe() and still shows after completion.
        protected override bool IsDone() {
            return HasConfiguredReactor();
        }

        private bool HasConfiguredReactor() {
            return S.Components.FindFirstMatching<MatterReactorComp>(r =>
                r.Tile.IsConstructed
                && r.Fuel != null && r.Fuel.IsConfigured
                && r.Tile.ENode != null && r.Tile.ENode.Grid.HasShipComputer) != null;
        }

        protected override void OnSubscribe() {
            S.Sig.UnconfiguredChanged.AddListener(OnUnconfChanged);
            A.S.UIQuery.ShowTutorialPopup.Ask(ConfigureReactorPage.PageId, 2f);
        }

        protected override void OnUnsubscribe() {
            S.Sig?.UnconfiguredChanged.RemoveListener(OnUnconfChanged);
        }

        private void OnUnconfChanged(ICanBeUnconfigured item, bool isUnconf) {
            if (!isUnconf) {
                RequestCheck();
            }
        }

    }
}
