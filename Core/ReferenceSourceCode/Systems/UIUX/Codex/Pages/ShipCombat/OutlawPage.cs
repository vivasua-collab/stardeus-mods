using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class OutlawPage : CodexConceptPage {
        public const string PageId = "ShipCombat/Outlaw";
        public override string Id => PageId;
        public override string TitleT => T.NounOutlaw;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints3_V3.F(
                CodexManualLink(T.NounClassPoints, ClassPointsPage.PageId),
                CodexManualLink(T.NounShipClass, ShipClassPage.PageId),
                Texts.Accented(T.NounOutlaw)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints5_V1.F(
                CodexManualLink(T.NounClassPoints, ClassPointsPage.PageId)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints6_V2.F(
                Texts.Accented(T.NounOutlaw),
                CodexManualLink(T.NounLawEnforcement, LawEnforcementPage.PageId)), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_ShipCombat_Outlaw, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutShipCombatStepClassPoints3_V3,
                T.TutShipCombatStepClassPoints6_V2,
                T.NounLawEnforcement, T.NounLawEnforcementOfficer);
        }
    }
}
