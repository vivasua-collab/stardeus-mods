using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class TimeControlsPage : CodexConceptPage {
        public const string PageId = "UI/TimeControls";
        public override string Id => PageId;
        public override string TitleT => T.InputGroupTime;
        protected override string GroupTitleT => T.NounUserInterface;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            if (!VirtualMouseInput.IsCurrent) {
                AddTextBlock(life, g, T.CodexMUiTimecontrolsDesc_V4.F(
                    BindingGlyph(ActionType.ToggleTime),
                    BindingGlyph(ActionType.TimeSpeedNormal),
                    BindingGlyph(ActionType.TimeSpeedFast1),
                    BindingGlyph(ActionType.TimeSpeedFast2)), idx++);
            }
            AddVideoBlock(life, g, CodexResourceId.Videos_UI_TimeControls, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMUiTimecontrolsDesc_V4);
        }
    }
}
