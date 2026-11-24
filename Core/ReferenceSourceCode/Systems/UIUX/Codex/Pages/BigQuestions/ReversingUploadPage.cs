using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ReversingUploadPage : CodexConceptPage {
        public const string PageId = "BigQuestions/ReversingUpload";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBigquestionsReversingupload;
        protected override string GroupTitleT => T.CodexMBigquestions;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBigquestionsReversinguploadDesc, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBigquestionsReversinguploadDesc);
        }
    }
}
