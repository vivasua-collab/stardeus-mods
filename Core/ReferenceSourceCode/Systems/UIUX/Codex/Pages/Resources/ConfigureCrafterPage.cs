using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ConfigureCreafterPage : CodexConceptPage {
        public const string PageId = "Resources/ConfigureCrafter";
        public override string Id => PageId;
        public override string TitleT => T.CodexMConfigureCrafter;
        protected override string GroupTitleT => T.CodexMResourcesProduction;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var newOrderText = $"{GlyphMap.SpritePlusThin} {T.CraftOrderNew}";
            AddTextBlock(life, g, T.CodexMConfigureCrafter1_V1.F(Texts.Accented(newOrderText)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Production_ConfiguringCrafter, idx++);
            AddTextBlock(life, g, T.CodexMConfigureCrafter2, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.ObjectsDevicesAssembler,
                T.ObjectsDevicesCraftingstation, "Pipeline");
        }
    }
}
