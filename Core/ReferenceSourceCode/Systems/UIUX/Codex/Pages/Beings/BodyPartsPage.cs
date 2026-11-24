using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class BodyPartsPage : CodexConceptPage {
        public const string PageId = "Beings/BodyParts";
        public override string Id => PageId;
        public override string TitleT => T.TutBodypartsStep1Title;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutBodypartsStep1_1, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_BodyParts, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutBodypartsStep1_1);
        }
    }
}
