using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class DatoidsPage : CodexConceptPage {
        public const string PageId = "Research/Datoids";
        public override string Id => PageId;
        public override string TitleT => T.MenuDatoids;
        protected override string GroupTitleT => T.CodexMResearch;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMResearchDatoidsDesc_V1.F(
                OverlayTip(s, "Research")), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_Research_Datoids, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Research_Datoids, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMResearchDatoidsDesc_V1);
        }
    }
}
