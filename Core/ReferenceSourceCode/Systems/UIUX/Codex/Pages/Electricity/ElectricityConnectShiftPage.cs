using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ElectricityConnectShift : CodexConceptPage {
        public const string PageId = "Electricity/ElectricityConnectShift";
        public override string Id => PageId;
        public override string TitleT {
            get {
                var glyph = CodexUtils.ShiftGlyph;
                return T.TutElConnectingWithKey_V1.F(glyph);
            }
        }
        protected override string GroupTitleT => T.CodexMElectricity;
        public override string[] LinkedGroups => new[] { "Controls" };

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.AppendLine(T.TutElConnectingWithKeyDesc);
            sb.Append("1. ");
            sb.AppendLine(T.TutSelectObj_V1.F(Texts.Accented(T.ResearchEnergyConnector)));
            sb.Append("2. ");
            sb.AppendLine(T.TutHoldButton_V1.F(CodexUtils.ShiftGlyph));
            sb.Append("3. ");
            sb.AppendLine(T.TutPressButtonOnObject_V2.F(CodexUtils.ClickGlyph, Texts.Accented(T.NounDevice)));
            AddTextBlock(life, g, sb.ToString(), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_ConnectDevice_Shift, idx++);

            AddTextBlock(life, g, T.TutElMassConnectShift_V1.F(CodexUtils.ShiftGlyph), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_ConnectDevices_Mass, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.MenuTutorials);
        }
    }
}
