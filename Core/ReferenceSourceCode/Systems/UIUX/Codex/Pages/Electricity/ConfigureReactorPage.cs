using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ConfigureReactorPage : CodexConceptPage {
        public const string PageId = "Electricity/ConfigureReactor";
        public override string Id => PageId;
        public override string TitleT => T.TutElConfigReactor;
        protected override string GroupTitleT => T.CodexMElectricity;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var desc = T.TutElConfigReactorDesc_V3.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesMatterReactorMini, T.MatterReactor),
                Texts.LinkToCodex(DefIdsH.ObjMaterialsScrapMetal), Texts.LinkToCodex(DefIdsH.ObjMaterialsSteelPlate));
            AddTextBlock(life, g, desc, idx++);
            var sb = Caches.StringBuilder;
            sb.Clear();
            sb.Append("1. ");
            sb.AppendLine(T.TutPressButtonOnObject_V2.F(CodexUtils.ClickGlyph, Texts.LinkToCodex(DefIdsH.ObjectsDevicesMatterReactorMini, T.MatterReactor)));
            sb.Append("2. ");
            sb.AppendLine(T.TutElUseUiToSelectMaterial);
            AddTextBlock(life, g, sb.ToString(), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_ConfigureReactor, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.Refill, T.MatterReactor, T.Fuel);
        }
    }
}
