using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class MindTransferPage : CodexConceptPage {
        public const string PageId = "Beings/MindTransfer";
        public override string Id => PageId;
        public override string TitleT => T.MindTransfer;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutBodypartsStep1_5_V1.F(GlyphMap.SpriteSkull), idx++);
            AddTextBlock(life, g, T.TutBodypartsStep4_1, idx++);
            AddTextBlock(life, g, T.TutBodypartsStep4_2, idx++);
            AddTextBlock(life, g, T.TutBodypartsStep4_3, idx++);
            AddTextBlock(life, g, T.TutBodypartsStep4_4, idx++);
            AddTextBlock(life, g, T.TutBodypartsStep5_3, idx++);
            AddTextBlock(life, g, T.TutBodypartsStep5_6, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_MindTransfer, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutBodypartsStep1_5_V1,
                T.TutBodypartsStep4_2,
                T.TutBodypartsStep4_3,
                T.TutBodypartsStep4_4,
                T.TutBodypartsStep5_3);
        }
    }
}
