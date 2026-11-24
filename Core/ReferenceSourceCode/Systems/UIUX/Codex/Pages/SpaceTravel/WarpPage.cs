using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class WarpPage : CodexConceptPage {
        public const string PageId = "SpaceTravel/Warp";
        public override string Id => PageId;
        public override string TitleT => T.VerbWarp;
        protected override string GroupTitleT => T.TutSpacetravelName;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMSpacetravelWarp, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_SpaceTravel_Warp, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.ObjectsDevicesWarpdrive, T.ObjectsDevicesWarpdrivemini,
                T.CodexMSpacetravelWarp);
        }
    }
}
