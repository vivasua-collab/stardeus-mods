using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class TeleportersPage : CodexConceptPage {
        public const string PageId = "BaseBuilding/Teleporters";
        public override string Id => PageId;
        public override string TitleT => T.CodexMBasebuildingTeleporters;
        protected override string GroupTitleT => T.CodexMBasebuilding;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMBasebuildingTeleporters1_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesTeleporter)), idx++);
            AddTextBlock(life, g, T.CodexMBasebuildingTeleporters2, idx++);
            AddTextBlock(life, g, T.CodexMBasebuildingTeleporters3, idx++);
            AddTextBlock(life, g, T.CodexMBasebuildingTeleporters4_V1.F(
                Texts.TitleWithShortcutGlyph(Texts.Accented(T.ToolConnect),
                    Input.ActionType.ShortcutEnergyConnect)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_BaseBuilding_Teleporters, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMBasebuildingTeleporters1_V1);
        }
    }
}
