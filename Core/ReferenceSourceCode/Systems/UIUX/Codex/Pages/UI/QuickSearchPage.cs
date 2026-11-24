using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class QuickSearchPage : CodexConceptPage {
        public const string PageId = "UI/QuickSearch";
        public override string Id => PageId;
        public override string TitleT => T.InputQuicksearch;
        protected override string GroupTitleT => T.NounUserInterface;
        public override string[] LinkedGroups => new[] { "BaseBuilding" };

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMUiQuicksearchDesc_V1.F(
                BindingGlyph(ActionType.QuickSearch)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_QuickSearch, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMUiQuicksearchDesc_V1);
        }
    }
}
