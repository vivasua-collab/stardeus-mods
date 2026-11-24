using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ShieldsPage : CodexConceptPage {
        public const string PageId = "ShipCombat/Shields";
        public override string Id => PageId;
        public override string TitleT => T.NounShields;
        protected override string GroupTitleT => T.NounShipCombat;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.TutShipCombatStepDevicesShield1_V2.F(
                Texts.Accented(T.NounShields),
                DefLink(DefIds.ObjectsDevicesShieldEmitter)), idx++);
            AddTextBlock(life, g, T.TutShipCombatStepDevicesShield2_V2.F(
                Texts.Accented(T.NounShields),
                CodexManualLink(T.FluxHard, FluxPage.PageId)), idx++);
            var txt =
            T.CodexMShieldsOverbuild_V2.F(
                DefLink(DefIds.ObjectsDevicesShieldEmitter),
                CodexManualLink(T.NounClassPoints, ClassPointsPage.PageId));
            AddTextBlock(life, g, $"{Texts.Bold(Texts.Red(T.Warning))}: {txt}" , idx++);
            AddImageBlock(life, g, CodexResourceId.Images_ShipCombat_Shields, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.TutShipCombatStepDevicesShield1_V2,
                T.TutShipCombatStepDevicesShield2_V2,
                T.ObjectsDevicesShieldemitter);
        }
    }
}
