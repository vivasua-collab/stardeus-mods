using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ModifiersPage : CodexConceptPage {
        public const string PageId = "Controls/Modifiers";
        public override string Id => PageId;
        public override string TitleT => T.CodexMInputcontrolsModifiers;
        protected override string GroupTitleT => T.NounControls;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMInputcontrolsModifiersDesc_V3.F(
                BindingGlyph(ActionType.CtrlModifier),
                BindingGlyph(ActionType.ShiftModifier),
                BindingGlyph(ActionType.AltModifier)), idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMInputcontrolsModifiersDesc_V3);
        }
    }
}
