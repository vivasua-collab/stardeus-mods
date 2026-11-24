using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class PressurizingAreasPage : CodexConceptPage {
        public const string PageId = "BaseBuilding/PressurizingAreas";
        public override string Id => PageId;
        public override string TitleT => T.CodexMPressurizingAreas;
        protected override string GroupTitleT => T.CodexMBasebuilding;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, Texts.LinkToCodexManual(T.CodexMPressurizingAreasStep1,
                EnclosingAreasPage.PageId), idx++);
            AddTextBlock(life, g, T.CodexMPressurizingAreasStep2_V1.F(
                Texts.TitleWithShortcutGlyph(Texts.Accented(T.ToolRepair), Input.ActionType.ShortcutRepairTiles)), idx++);
            AddTextBlock(life, g, T.CodexMPressurizingAreasStep3_V1.F(
                Texts.TitleWithShortcutGlyph(Texts.Accented(T.ToolConnect), Input.ActionType.ShortcutEnergyConnect)), idx++);
            AddTextBlock(life, g, T.CodexMPressurizingAreasStep4_V1.F(
                Texts.LinkToCodex(DefIdsH.StructureWallsVent01)), idx++);
            AddTextBlock(life, g, T.CodexMPressurizingAreasStep5_V2.F(
                Texts.LinkToOverlay(OverlayId.AtmoO2Res),
                Texts.LinkToOverlay(OverlayId.AtmoTempRes)
                ), idx++);

            // No heaters or oxygen pumps here: the tutorial shows this page while sealing the
            // stasis room, before Atmosphere Control is researched (STA-1776). Creating
            // Atmosphere covers them.
            AddVideoBlock(life, g, CodexResourceId.Videos_BaseBuilding_MakingAirtight, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.OverlayTipAirtight, T.OverlayTipInsulation, T.EnclosedAreas);
        }
    }
}
