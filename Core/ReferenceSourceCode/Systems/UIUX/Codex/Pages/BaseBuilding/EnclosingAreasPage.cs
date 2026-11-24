using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class EnclosingAreasPage : CodexConceptPage {
        public const string PageId = "BaseBuilding/EnclosingAreas";
        public override string Id => PageId;
        public override string TitleT => T.CodexMEnclosingAreas;
        protected override string GroupTitleT => T.CodexMBasebuilding;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMEnclosingAreasStep1, idx++);
            AddTextBlock(life, g, T.CodexMEnclosingAreasStep2, idx++);
            AddTextBlock(life, g, T.CodexMEnclosingAreasStep3_V1.F(
                Texts.LinkToOverlay(OverlayId.Areas)), idx++);
            AddTextBlock(life, g, T.CodexMEnclosingAreasStep4_V2.F(
                Texts.LinkToOverlay(OverlayId.AtmoO2Res),
                Texts.LinkToOverlay(OverlayId.AtmoTempRes)
                ), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_BaseBuilding_EncloseArea, idx++);
            var link = Texts.LinkToCodexManual(T.LearnHowToAirtight, PressurizingAreasPage.PageId);
            var txt = $"{T.CodexMEnclosingAreasStep5} {link}";
            AddTextBlock(life, g, txt, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.EnclosedAreas, T.Room);
        }
    }
}
