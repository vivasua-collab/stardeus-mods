using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ClassPointsPage : CodexConceptPage {
        public const string PageId = "ShipCombat/ClassPoints";
        public override string Id => PageId;
        public override string TitleT => T.NounClassPoints;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.ClassPointsDesc, idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints1_V1.F(T.NounClassPoints), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints2_V1.F(T.NounClassPoints), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints3_V3.F(T.NounClassPoints,
                CodexManualLink(T.NounShipClass, ShipClassPage.PageId),
                CodexManualLink(T.NounOutlaw, OutlawPage.PageId)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints5_V1.F(
                T.NounClassPoints), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints6_V2.F(
                CodexManualLink(T.NounOutlaw, OutlawPage.PageId),
                CodexManualLink(T.NounLawEnforcement, LawEnforcementPage.PageId)), idx++);
            AddTextBlock(life, g, $"{T.VerbSee}: {Texts.LinkToShipComputerApp(ShipComputerApp_ShipConfig.AppId, UIId.ShipConfig)} → {T.NounClassPoints}",
                idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_ClassPoints, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.ClassPointsDesc);
        }
    }
}
