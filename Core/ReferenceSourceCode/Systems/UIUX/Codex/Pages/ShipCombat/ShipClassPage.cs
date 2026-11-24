using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ShipClassPage : CodexConceptPage {
        public const string PageId = "ShipCombat/ShipClass";
        public override string Id => PageId;
        public override string TitleT => T.NounShipClass;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutShipCombatStepClass1_V1.F(
                Texts.Accented(T.NounShipClass)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClass2_V1.F(
                Texts.Accented(T.NounShipClass)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepClassPoints1_V1.F(
                CodexManualLink(T.NounClassPoints, ClassPointsPage.PageId)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_ShipClass, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutShipCombatStepClass1_V1,
                T.TutShipCombatStepClass2_V1,
                T.TutShipCombatStepClassPoints1_V1);
        }
    }
}
