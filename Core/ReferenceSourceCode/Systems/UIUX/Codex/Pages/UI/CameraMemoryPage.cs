using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class CameraMemoryPage : CodexConceptPage {
        public const string PageId = "UI/CameraMemory";
        public override string Id => PageId;
        public override string TitleT => T.CodexMUiCameramemory;
        protected override string GroupTitleT => T.NounUserInterface;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMUiCameramemoryDesc_V1.F(
                BindingGlyph(ActionType.CtrlModifier)), idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMUiCameramemoryDesc_V1);
        }
    }
}
