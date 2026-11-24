using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class CombatDraftingPage : CodexConceptPage {
        public const string PageId = "Beings/CombatDrafting";
        public override string Id => PageId;
        public override string TitleT => T.CodexMDrafting;
        protected override string GroupTitleT => T.Beings;
        // Draft and undraft share one entity action hotkey
        private static readonly string draftBindingId =
            EntityActionHotkeys.BindingId(DraftAction.ToggleDraftHotkeyId);

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMDraftingStep1_V1.F(
                The.Bindings.AddShortcutGlyphTo(Texts.Accented(T.VerbDraft), draftBindingId)), idx++);
            AddTextBlock(life, g, T.CodexMDraftingStep2_V1.F(
                BindingGlyph(Input.ActionType.ClickCancel)), idx++);
            AddTextBlock(life, g, T.CodexMDraftingStep4_V1.F(
                BindingGlyph(Input.ActionType.ShiftModifier)), idx++);
            AddTextBlock(life, g, T.CodexMDraftingStep3_V1.F(
                The.Bindings.AddShortcutGlyphTo(Texts.Accented(T.VerbUndraft), draftBindingId)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_CombatDrafting, idx++);

            AddTextBlock(life, g, $"{T.VerbSeeAlso}: {CodexManualLink(T.TutCombatStep2Title, CombatReactionsPage.PageId)}"
                , idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query,
                T.CodexMDraftingStep1_V1,
                T.CodexMDraftingStep2_V1,
                T.CodexMDraftingStep4_V1,
                T.CodexMDraftingStep3_V1,
                T.CombatControls,
                T.VerbDraft,
                T.VerbUndraft);
        }
    }
}
