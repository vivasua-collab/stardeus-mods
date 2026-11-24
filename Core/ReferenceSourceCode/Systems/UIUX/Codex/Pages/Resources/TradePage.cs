using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class TradePage : CodexConceptPage {
        public const string PageId = "Resources/Trade";
        public override string Id => PageId;
        public override string TitleT => T.Trade;
        protected override string GroupTitleT => T.CodexMResourcesProduction;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMTrade1_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesStasisPod)
            ), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Production_Trade, idx++);
            AddTextBlock(life, g, T.CodexMTrade2, idx++);
            AddTextBlock(life, g, T.CodexMTrade3_V1.F(Texts.Accented(T.StockMarket)), idx++);
            AddTextBlock(life, g, T.CodexMTrade4_V2.F(
                Texts.LinkToUI(T.ResearchEconomyOnlineshop, UIId.Wallet),
                Texts.LinkToCodex(DefIdsH.ObjContainersStorageCapsule)
            ), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Production_OnlineShop, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMTrade1_V1,
                T.CodexMTrade2, T.CodexMTrade3_V1, T.CodexMTrade4_V2,
                T.StockMarket, T.ResearchEconomyOnlineshop, T.Buy, T.Sell);
        }
    }
}
