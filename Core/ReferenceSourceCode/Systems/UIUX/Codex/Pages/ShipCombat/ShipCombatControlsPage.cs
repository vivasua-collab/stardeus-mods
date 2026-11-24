using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ShipCombatControlsPage : CodexConceptPage {
        public const string PageId = "ShipCombat/ShipCombatControls";
        public override string Id => PageId;
        public override string TitleT => T.OverlayTipShipcombat;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var bsp = Texts.LinkToCodex(DefIdsH.ObjectsDevicesBattleStation,
                T.NounBattleStationPlural);

            AddTextBlock(life, g, T.CodexMShipCombatControlsStep1, idx++);
            AddTextBlock(life, g, T.CodexMShipCombatControlsStep2_V2.F(
                Texts.Accented(T.VerbFireWeaponShort), bsp), idx++);
            AddTextBlock(life, g, T.CodexMShipCombatControlsStep3_V2.F(
                Texts.LinkToCodexManual(T.FluxAccumulated, FluxPage.PageId),
                Texts.LinkToCodexManual(T.FluxOverload, FluxVentingPage.PageId)), idx++);
            AddTextBlock(life, g, T.CodexMShipCombatControlsStep4_V2.F(
                Texts.LinkToShipComputerApp(ShipComputerApp_ShipConfig.AppId, UIId.ShipConfig),
                CodexManualLink(T.NounBattleStationPlural, BattleStationsPage.PageId)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_ShipCombat_ShipCombatControls, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMShipCombatControlsStep1,
                T.CodexMShipCombatControlsStep2_V2,
                T.CodexMShipCombatControlsStep3_V2,
                T.CodexMShipCombatControlsStep4_V2,
                T.FluxAccumulated,
                T.NounBattleStationPlural,
                T.FluxOverload,
                T.VerbFireWeaponShort,
                T.VerbWeaponsFiringShort);
        }
    }
}
