using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class FluxPage : CodexConceptPage {
        public const string PageId = "ShipCombat/Flux";
        public override string Id => PageId;
        public override string TitleT => T.NounFlux;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.NounFluxDesc, idx++);
            var sb = Caches.StringBuilder3;
            sb.Clear();
            sb.AppendLine(T.TutShipCombatStepFluxTypes1_V1.F(Texts.Accented(T.NounFlux)));
            sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
            sb.AppendLine(T.TutShipCombatStepFluxTypes2_V1.F(Texts.Accented(T.FluxSoft)));
            sb.AppendFormat(" {0} ", GlyphMap.ArrowRight);
            sb.AppendLine(T.TutShipCombatStepFluxTypes3_V2.F(Texts.Accented(T.FluxHard),
                CodexManualLink(T.NounShields, ShieldsPage.PageId)));
            AddTextBlock(life, g, sb.ToString(), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepFluxTypes4_V1.F(
                Texts.Accented(T.FluxHard)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepFluxCapacity1_V4.F(
                Texts.Accented(T.FluxAccumulated),
                Texts.Accented(T.FluxHard),
                Texts.Accented(T.FluxSoft),
                Texts.Accented(T.FluxCapacity)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_Flux, idx++);
            AddTextBlock(life, g, T.TutShipCombatStepFluxCapacity3_V3.F(
                CodexManualLink(T.NounShipClass, ShipClassPage.PageId),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesFluxCapacitor, T.NounFluxCapacitors),
                Texts.Accented(T.FluxCapacity)), idx++);
            AddTextBlock(life, g, T.CodexMFluxventBuild_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesFluxVent, T.NounFluxVents)), idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.NounFluxDesc, T.FluxSoft, T.FluxHard,
                T.FluxAccumulated, T.FluxCapacity);
        }
    }
}
