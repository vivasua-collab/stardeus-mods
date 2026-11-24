using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class WinchingPage : CodexConceptPage {
        public const string PageId = "BaseBuilding/Winching";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBasebuildingWinching;
        protected override string GroupTitleT => T.CodexMBasebuilding;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var desc =
            T.CodexMBasebuildingWinchingDesc_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesWinchHook),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesWinchAnchor));
            AddTextBlock(life, g, desc, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_BaseBuilding_Winching, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBasebuildingWinchingDesc_V2.F(
                T.ObjectsDevicesWinchanchor, T.ObjectsDevicesWinchhook));
        }
    }
}
