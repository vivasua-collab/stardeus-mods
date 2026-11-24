using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ShipComputerAppsPage : CodexConceptPage {
        public const string PageId = "UI/ShipComputerApps";
        public override string Id => PageId;
        public override string TitleT => $"{T.ResearchShipcomputer} {GlyphMap.ArrowRight} {T.NounAppPlural}";
        protected override string GroupTitleT => T.NounUserInterface;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMShipComputerApps_V2.F(
                Texts.TitleWithShortcutGlyph(
                    Texts.Accented(T.NounAppPlural), ActionType.ShortcutApps),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShipComputer)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_ClassPoints, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMShipComputerApps_V2);
        }
    }
}
