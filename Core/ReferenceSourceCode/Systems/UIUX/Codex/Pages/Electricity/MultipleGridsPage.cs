using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class MultipleGridsPage : CodexConceptPage {
        public const string PageId = "Electricity/MultipleGrids";
        public override string Id => PageId;
        public override string TitleT => T.CodexMElectricityMultiplegrids;
        protected override string GroupTitleT => T.CodexMElectricity;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMElectricityMultiplegridsDesc, idx++);
            AddTextBlock(life, g, T.TutFindingUnamangedGrids, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_FindGrids, idx++);
            AddTextBlock(life, g, $"{T.VerbSee}: {Texts.LinkToCodexManual(T.TutUnifyingElGrid, UnifyingGridPage.PageId)}", idx++);

        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMElectricityMultiplegridsDesc);
        }
    }
}
