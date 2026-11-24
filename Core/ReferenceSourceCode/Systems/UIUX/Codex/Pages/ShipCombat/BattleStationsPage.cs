using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class BattleStationsPage : CodexConceptPage {
        public const string PageId = "ShipCombat/BattleStations";
        public override string Id => PageId;
        public override string TitleT => T.NounBattleStationPlural;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBattlestationsStep1_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation,
                T.NounBattleStationPlural)), idx++);
            AddTextBlock(life, g, T.CodexMBattlestationsStep2_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation)
            ), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_ConfigureBattleStation, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBattlestationsStep1_V1,
                T.CodexMBattlestationsStep2_V1);
        }
    }
}
