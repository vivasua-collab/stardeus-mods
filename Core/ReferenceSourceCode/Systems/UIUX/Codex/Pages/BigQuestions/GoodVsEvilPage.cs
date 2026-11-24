using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class GoodVsEvilPage : CodexConceptPage {
        public const string PageId = "BigQuestions/GoodVsEvil";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBigquestionsGoodvsevil;
        protected override string GroupTitleT => T.CodexMBigquestions;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBigquestionsGoodvsevilDesc, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBigquestionsGoodvsevilDesc);
        }
    }
}
