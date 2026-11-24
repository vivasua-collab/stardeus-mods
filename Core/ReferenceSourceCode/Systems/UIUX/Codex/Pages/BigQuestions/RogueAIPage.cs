using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class RogueAIPage : CodexConceptPage {
        public const string PageId = "BigQuestions/RogueAI";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBigquestionsRogueai;
        protected override string GroupTitleT => T.CodexMBigquestions;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBigquestionsRogueaiDesc, idx++);
            AddTextBlock(life, g, T.CodexMBigquestionsRogueaiOverride, idx++);
            AddTextBlock(life, g, T.CodexMBigquestionsRogueaiDoctrines, idx++);
            AddTextBlock(life, g, T.CodexMBigquestionsRogueaiIntrusion, idx++);
            AddTextBlock(life, g, T.CodexMBigquestionsRogueaiCost, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query,
                T.CodexMBigquestionsRogueaiDesc,
                T.CodexMBigquestionsRogueaiOverride,
                T.CodexMBigquestionsRogueaiDoctrines,
                T.CodexMBigquestionsRogueaiIntrusion,
                T.CodexMBigquestionsRogueaiCost);
        }
    }
}
