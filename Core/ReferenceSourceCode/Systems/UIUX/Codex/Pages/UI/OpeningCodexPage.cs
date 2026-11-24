using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class OpeningCodexPage : CodexConceptPage {
        public const string PageId = "UI/OpeningCodex";
        public override string Id => PageId;
        public override string TitleT => T.CodexManual;
        protected override string GroupTitleT => T.NounUserInterface;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMUiOpeningcodexIntro_V1.F(
                Texts.Accented(T.CodexManual)), idx++);
            AddTextBlock(life, g, T.CodexMUiOpeningcodexWhere_V3.F(
                Texts.TitleWithShortcutGlyph(
                    Texts.Accented(T.NounAppPlural), ActionType.ShortcutApps),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesShipComputer),
                Texts.TitleWithShortcutGlyph(
                    Texts.Accented(T.InputQuicksearch), ActionType.QuickSearch)), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_UI_CodexSequence, idx++);
            AddTextBlock(life, g, T.CodexMUiOpeningcodexLearned, idx++);
            // AddVideoBlock(life, g, CodexResourceId.Videos_UI_Codex, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.OpenCodex, T.CodexMUiOpeningcodexIntro_V1,
                T.CodexMUiOpeningcodexWhere_V3, T.CodexMUiOpeningcodexLearned);
        }
    }
}
