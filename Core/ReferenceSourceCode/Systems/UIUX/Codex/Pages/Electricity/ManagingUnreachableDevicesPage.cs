using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class TempConnectionPage : CodexConceptPage {
        public const string PageId = "Electricity/TempConnection";
        public override string Id => PageId;
        public override string TitleT => T.TutElTempConnect;
        protected override string GroupTitleT => T.CodexMElectricity;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMTempconn, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_TempConnection, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutElTempConnect, T.TutElTempDisconnect);
        }
    }
}
