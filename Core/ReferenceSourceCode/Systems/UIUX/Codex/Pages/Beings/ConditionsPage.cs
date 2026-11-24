using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ConditionsPage : CodexConceptPage {
        public const string PageId = "Beings/Conditions";
        public override string Id => PageId;
        public override string TitleT => T.NounConditions;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutBodypartsStep3_1, idx++);
            AddTextBlock(life, g, T.TutBodypartsStep3_2_V1.F(GlyphMap.SpriteSkull), idx++);
            AddTextBlock(life, g, T.TutBodypartsStep3_3, idx++);
            AddImageBlock(life, g, CodexResourceId.Images_UI_Conditions, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutBodypartsStep3_1, T.TutBodypartsStep3_2_V1,
                T.TutBodypartsStep3_3);
        }
    }
}
