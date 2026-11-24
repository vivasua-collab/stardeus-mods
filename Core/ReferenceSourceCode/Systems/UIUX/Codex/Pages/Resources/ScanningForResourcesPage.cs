using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ScanningForResourcesPage : CodexConceptPage {
        public const string PageId = "Resources/ScanningForResources";
        public override string Id => PageId;
        public override string TitleT => T.CodexMScanningResources;
        protected override string GroupTitleT => T.CodexMResourcesProduction;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMScanningResources1_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesRadar),
                Texts.LinkToUI(T.Starmap, UIId.Starmap)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Production_WideScan, idx++);
            AddTextBlock(life, g, T.CodexMScanningResources2, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Production_DeepScan, idx++);
            AddTextBlock(life, g, T.CodexMScanningResources3_V1.F(
                Texts.LinkToResearch(DefIdsH.ResearchScanningSystem, T.ResearchSystemscanning)), idx++);
            AddTextBlock(life, g, T.CodexMScanningResources4_V3.F(
                Texts.LinkToResearch(DefIdsH.ResearchScanningExtended, T.SystemScanExtended),
                Texts.LinkToCodex(DefIdsH.ObjMaterialsDarkMatter),
                Texts.LinkToCodex(DefIdsH.ObjMaterialsEdenium)), idx++);
            AddTextBlock(life, g, T.CodexMScanningResources5_V2.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesRadar),
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesSystemScanner)), idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMScanningResources1_V2,
                T.CodexMScanningResources2,
                T.CodexMScanningResources3_V1,
                T.CodexMScanningResources4_V3,
                T.CodexMScanningResources5_V2,
                T.Radar, T.ObjectsDevicesSystemscanner,
                    T.SystemScanExtended, T.SystemScanAdvanced,
                T.ObjMaterialsEdenium, T.ObjMaterialsAerolith, T.ObjMaterialsDarkmatter,
                T.ObjMaterialsHydronium,
                T.ObjMaterialsTerranite);
        }
    }
}
