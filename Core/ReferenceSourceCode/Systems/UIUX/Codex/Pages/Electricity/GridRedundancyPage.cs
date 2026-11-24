using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class GridRedundancyPage : CodexConceptPage {
        public const string PageId = "Electricity/GridRedundancy";
        public override string Id => PageId;
        public override string TitleT => T.CodexMElectricityGridredundancy;
        protected override string GroupTitleT => T.CodexMElectricity;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMElectricityGridredundancyDesc, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMElectricityGridredundancyDesc);
        }
    }
}
