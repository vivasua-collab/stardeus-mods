using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ShipTagPage : CodexConceptPage {
        public const string PageId = "ShipCombat/ShipTag";
        public override string Id => PageId;
        public override string TitleT => T.CodexShipTags;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMShiptags, idx++);
            AddTextBlock(life, g, T.CodexMShiptags2_V1.F(Texts.Accented(T.CodexShipTags)), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_ShipCombat_ShipTag, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMShiptags);
        }
    }
}
