using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class WhatAmIPage : CodexConceptPage {
        public const string PageId = "BigQuestions/WhatAmI";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBigquestionsWhatami;
        protected override string GroupTitleT => T.CodexMBigquestions;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBigquestionsWhatamiDesc, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBigquestionsWhatamiDesc);
        }
    }
}
