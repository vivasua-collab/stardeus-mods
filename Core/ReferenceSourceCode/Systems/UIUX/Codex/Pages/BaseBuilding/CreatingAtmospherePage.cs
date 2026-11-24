using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class CreatingAtmospherePage : CodexConceptPage {
        public const string PageId = "BaseBuilding/CreatingAtmosphere";
        public override string Id => PageId;
        public override string TitleT => T.CodexMCreatingAtmosphere;
        protected override string GroupTitleT => T.CodexMBasebuilding;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var step1 = T.CodexMCreatingAtmosphereStep1;
            var enc = Texts.LinkToCodexManual(T.CodexMEnclosingAreas, EnclosingAreasPage.PageId);
            var press = Texts.LinkToCodexManual(T.CodexMPressurizingAreas, PressurizingAreasPage.PageId);

            step1 = $"{step1} {T.VerbSee}: {enc}, {press}";
            AddTextBlock(life, g, step1, idx++);

            var step2 = T.CodexMCreatingAtmosphereStep2_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesHeaterMini, T.ObjectsDevicesHeater),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesOxygenPumpMini, T.ObjectsDevicesOxygenpump));
            AddTextBlock(life, g, step2, idx++);

            var step3 = T.CodexMCreatingAtmosphereStep3_V2.F(
                Texts.LinkToOverlay(OverlayId.AtmoO2),
                Texts.LinkToOverlay(OverlayId.AtmoTemp));
            AddTextBlock(life, g, step3, idx++);

            AddTextBlock(life, g, T.CodexMPressurizingAreasStep5_V2.F(
                Texts.LinkToOverlay(OverlayId.AtmoO2Res),
                Texts.LinkToOverlay(OverlayId.AtmoTempRes)
                ), idx++);

            AddVideoBlock(life, g, CodexResourceId.Videos_BaseBuilding_CreatingAtmosphere, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.OverlayTipAirtight, T.OverlayTipInsulation, T.EnclosedAreas);
        }
    }
}
