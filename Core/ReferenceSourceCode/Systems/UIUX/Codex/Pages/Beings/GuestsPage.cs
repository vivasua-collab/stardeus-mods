using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class GuestsPage : CodexConceptPage {
        public const string PageId = "Beings/Guests";
        public override string Id => PageId;
        public override string TitleT => T.NounGuests;
        protected override string GroupTitleT => T.Beings;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMGuests1, idx++);
            AddTextBlock(life, g, T.CodexMGuests2, idx++);
            AddTextBlock(life, g, T.CodexMGuests3_V1.F(Texts.Accented(T.VerbSendBeings)), idx++);
            AddTextBlock(life, g, T.CodexMGuests4_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesTradePortal)
            ), idx++);
            AddTextBlock(life, g, T.CodexMGuests5, idx++);
            AddTextBlock(life, g, T.CodexMGuests6, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Beings_RescueGuests, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMGuests1, T.CodexMGuests2,
                T.CodexMGuests3_V1, T.CodexMGuests4_V1, T.CodexMGuests5, T.CodexMGuests6,
                T.VerbSendBeings);
        }
    }
}
