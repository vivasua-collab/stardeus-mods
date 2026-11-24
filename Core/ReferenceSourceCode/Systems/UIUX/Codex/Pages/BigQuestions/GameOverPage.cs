using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class GameOverPage : CodexConceptPage {
        public const string PageId = "BigQuestions/GameOver";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBigquestionsGameover;
        protected override string GroupTitleT => T.CodexMBigquestions;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBigquestionsGameoverDesc, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBigquestionsGameoverDesc);
        }
    }
}
