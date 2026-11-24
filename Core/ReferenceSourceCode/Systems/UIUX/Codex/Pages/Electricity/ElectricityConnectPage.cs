using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ElectricityConnectPage : CodexConceptPage {
        public const string PageId = "Electricity/ElectricityConnect";
        public override string Id => PageId;
        public override string TitleT => T.TutElConnectTitle;
        protected override string GroupTitleT => T.CodexMElectricity;
        public override string[] LinkedGroups => new[] { "Controls" };

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            var connectToolName = Texts.TitleWithShortcutGlyph(Texts.Accented(T.ToolConnect), ActionType.ShortcutEnergyConnect);
            sb.Append("1. ");
            sb.AppendLine(T.TutActivateTool_V1.F(Texts.NoBr(connectToolName)));
            sb.Append("2. ");
            sb.AppendLine(
                T.TutPressButtonOnObject_V2.F(Texts.NoBr(CodexUtils.ClickGlyph),
                    Texts.NoBr(Texts.LinkToCodex(DefIdsH.ObjectsDevicesEnergyConnector))));
            sb.Append("3. ");
            sb.AppendLine(T.TutDragConnectLine);
            sb.Append("4. ");
            sb.AppendLine(
                T.TutReleaseButtonToConnect_V1.F(Texts.NoBr(CodexUtils.ClickGlyph)));
            AddTextBlock(life, g, sb.ToString(), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_ConnectDevice, idx++);
            AddTextBlock(life, g,
                T.TutElConnectCpuWarning_V1.F(Texts.NoBr(Texts.LinkToCodex(DefIdsH.ObjectsDevicesShipComputer))), idx++);

            AddTextBlock(life, g, T.TutElDisconnect, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_DisconnectDevice, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.MenuTutorials);
        }
    }
}
