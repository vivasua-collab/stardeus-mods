using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class JobsPage : CodexConceptPage {
        public const string PageId = "Beings/Jobs";
        public override string Id => PageId;
        public override string TitleT => T.CodexMJobsTitle;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMJobs1, idx++);
            AddImageBlock(life, g, CodexResourceId.Images_UI_CantAssign, idx++);
            AddTextBlock(life, g, T.CodexMJobs2, idx++);
            AddImageBlock(life, g, CodexResourceId.Images_UI_Jobs, idx++);
            AddTextBlock(life, g, T.CodexMJobsPriorities, idx++);
            AddTextBlock(life, g, T.CodexMJobs4_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjPartsMobilizerMiniJet),
                Texts.LinkToCodex(DefIdsH.ObjEquipmentClothingSpaceSuit02)
                ), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_UI_Abilities, idx++);
            AddTextBlock(life, g, T.CodexMJobs5_V5.F(
                Texts.LinkToCodex(DefIdsH.ObjPartsCoreDumbCore),
                Texts.LinkToCodex(DefIdsH.ObjPartsCoreLiteCore),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesResearchStation),
                Texts.LinkToCodex(DefIdsH.ObjPartsCoreMiniCore),
                Texts.LinkToCodex(DefIdsH.ObjPartsBrainBrainHuman)
            ), idx++);
            AddTextBlock(life, g, T.CodexMJobs6_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesMLBooth)), idx++);
            AddTextBlock(life, g, T.CodexMJobs3_V1.F(Texts.LinkToShipComputerApp(
                ShipComputerApp_JobMatrix.AppId, UIId.JobMatrix)), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_UI_JobMatrix, idx++);
            AddTextBlock(life, g, T.CodexMJobs7_V1.F(The.Bindings.GetControlName(ActionType.ClickCancel)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_ForceAssignTask, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutBodypartsStep3_1, T.TutBodypartsStep3_2_V1,
                T.TutBodypartsStep3_3);
        }
    }
}
