using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class HyperjumpPage : CodexConceptPage {
        public const string PageId = "SpaceTravel/Hyperjump";
        public override string Id => PageId;
        public override string TitleT => T.NounHyperjump;
        protected override string GroupTitleT => T.TutSpacetravelName;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutSpacetravelStep1_3_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesFusionDrive),
                Texts.LinkToCodex(DefIdsH.ObjMaterialsFusionCell)
            ), idx++);
            AddTextBlock(life, g, T.TutSpacetravelStep4_5_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesSolarPanel, T.ResearchEnergySolar)
            ), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_SpaceTravel_Hyperjump, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutSpacetravelStep1_3_V2,
                T.TutSpacetravelStep4_5_V1);
        }
    }
}
