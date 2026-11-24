using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class UnifyingGridPage : CodexConceptPage {
        public const string PageId = "Electricity/UnifyingGrid";
        public override string Id => PageId;
        public override string TitleT => T.TutUnifyingElGrid;
        protected override string GroupTitleT => T.CodexMElectricity;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            var desc = T.TutUnifyingElGridDesc_V1.F(Texts.LinkToCodex(DefIdsH.ObjectsDevicesShipComputer));
            AddTextBlock(life, g, desc, idx++);
            AddImageBlock(life, g, CodexResourceId.Images_Electricity_GridUnreachable, idx++);

            AddTextBlock(life, g, T.TutUnifyingElGridStep1, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_UnifyGrid, idx++);
            AddTextBlock(life, g, $"{T.VerbSee}: {Texts.LinkToCodexManual(T.CodexMElectricityMultiplegrids, MultipleGridsPage.PageId)}", idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, $"{T.EnergyNodeUnreachable}{T.ElectricityGrid}");
        }
    }
}
