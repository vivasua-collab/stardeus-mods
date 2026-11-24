using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class CombatReactionsPage : CodexConceptPage {
        public const string PageId = "Beings/CombatReactions";
        public override string Id => PageId;
        public override string TitleT => T.TutCombatStep2Title;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutCombatReactions, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_CombatReaction, idx++);
            AddTextBlock(life, g, $"{T.VerbSeeAlso}: {CodexManualLink(T.CodexMDrafting, CombatDraftingPage.PageId)}"
                , idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutCombatReactions);
        }
    }
}
