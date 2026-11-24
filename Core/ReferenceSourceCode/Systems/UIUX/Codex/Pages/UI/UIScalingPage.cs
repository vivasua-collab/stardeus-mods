using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class UIScalingPage : CodexConceptPage {
        public const string PageId = "UI/UIScaling";
        public override string Id => PageId;
        public override string TitleT => T.CodexMUiUiscaling;
        protected override string GroupTitleT => T.NounUserInterface;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMUiUiscalingDesc, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_Scaling, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMUiUiscalingDesc);
        }
    }
}
