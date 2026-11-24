using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class CameraControlsPage : CodexConceptPage {
        public const string PageId = "Controls/CameraControls";
        public override string Id => PageId;
        public override string TitleT => T.TutBasicControlsStepCam;
        protected override string GroupTitleT => T.NounControls;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var moveGlyph = CodexUtils.GetControlName(ActionType.CameraMove);
            var zoomGlyph = CodexUtils.GetControlName(ActionType.CameraZoom);
            if (!VirtualMouseInput.IsCurrent) {
                var panChoice = $"{CodexUtils.GetControlName(ActionType.CameraDrag)} / {CodexUtils.CancelGlyph}";
                moveGlyph = T.ChoiceOr_V2.F(moveGlyph, panChoice);
            }
            AddTextBlock(life, g,
                T.TutBasicControlsStepCamDesc_V3.F(Texts.NoBr(moveGlyph),
                        Texts.NoBr(zoomGlyph), Texts.TMPColorGrey), idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutBasicControlsStepCamDesc_V3);
        }
    }
}
