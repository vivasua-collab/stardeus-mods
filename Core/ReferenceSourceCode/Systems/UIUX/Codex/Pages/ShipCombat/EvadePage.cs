using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class EvadePage : CodexConceptPage {
        public const string PageId = "ShipCombat/Evade";
        public override string Id => PageId;
        public override string TitleT => T.NounEvade;
        protected override string GroupTitleT => T.NounShipCombat;


        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.NounEvadeDesc, idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesEngines1_V4.F(
                DefLink(DefIds.ObjectsDevicesThruster),
                Texts.Accented(T.NounEvade),
                CodexManualLink(T.NounShipClass, ShipClassPage.PageId),
                TunablePercent("ShipMaxEvadePercent")), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_ShipCombat_Evade, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.NounEvadeDesc);
        }
    }
}
