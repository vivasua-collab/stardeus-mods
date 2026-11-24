using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class FluxVentingPage : CodexConceptPage {
        public const string PageId = "ShipCombat/FluxVenting";
        public override string Id => PageId;
        public override string TitleT => T.FluxVenting;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutShipCombatStepFluxOverload1_V3.F(
                CodexManualLink(T.FluxAccumulated, FluxPage.PageId),
                Texts.Accented(T.FluxCapacity),
                Texts.Accented(T.FluxOverload)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepFluxOverload2_V3.F(
                Texts.Accented(T.FluxOverload),
                CodexManualLink(T.NounShields, ShieldsPage.PageId),
                CodexManualLink(T.NounFlux, FluxPage.PageId)), idx++);
            AddTextBlock(life, g, T.CodexMFluxventManual_V2.F(
                CodexManualLink(T.NounFlux, FluxPage.PageId),
                Texts.Accented(T.FluxOverload)), idx++);
            AddTextBlock(life, g, T.CodexMFluxventBuild_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesFluxVent, T.NounFluxVents)
            ), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_Flux, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query,
            T.TutShipCombatStepFluxOverload1_V3,
            T.TutShipCombatStepFluxOverload2_V3,
            T.CodexManual,
            T.FluxOverload,
            T.NounFluxVents);
        }
    }
}
