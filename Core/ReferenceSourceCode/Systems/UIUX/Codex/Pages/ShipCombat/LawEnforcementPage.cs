using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class LawEnforcementPage : CodexConceptPage {
        public const string PageId = "ShipCombat/LawEnforcement";
        public override string Id => PageId;
        public override string TitleT => T.NounLawEnforcement;
        protected override string GroupTitleT => T.NounShipCombat;


        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.LawEnforcementDesc_V1.F(
                CodexManualLink(T.NounOutlaw, OutlawPage.PageId)), idx++);
            AddImageBlock(life, g,
                CodexResourceId.Images_ShipCombat_LawEnforcementOfficers, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.LawEnforcementDesc_V1,
                T.NounLawEnforcementOfficer, T.NounOutlaw);
        }
    }
}
