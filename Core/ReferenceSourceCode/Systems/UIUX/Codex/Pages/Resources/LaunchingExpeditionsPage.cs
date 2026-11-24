using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class LaunchingExpeditionsPage : CodexConceptPage {
        public const string PageId = "Resources/LaunchingExpeditions";
        public override string Id => PageId;
        public override string TitleT => T.CodexMLaunchingExpeditions;
        protected override string GroupTitleT => T.CodexMResourcesProduction;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMLaunchingExpeditionsStep1_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShuttle)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Production_MiningExpedition, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMLaunchingExpeditionsStep1_V1,
                T.Shuttle, T.Expedition, T.VerbMine, T.NounMiningRate,
                T.NounResources);
        }
    }
}
