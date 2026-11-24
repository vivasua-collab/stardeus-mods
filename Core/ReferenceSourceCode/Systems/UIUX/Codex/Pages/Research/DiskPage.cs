using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class DiskPage : CodexConceptPage {
        public const string PageId = "Research/Disk";
        public override string Id => PageId;
        public override string TitleT => T.CodexMResearchDisk;
        protected override string GroupTitleT => T.CodexMResearch;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMResearchDisk1_V1.F(Texts.LinkToUI(T.ResearchTree, UIId.Research)), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_Research_Disk, idx++);

            AddTextBlock(life, g, T.CodexMResearchDisk2_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjectsDevicesDiskModuleMini, T.NounDiskModulesPlural)), idx++);
            AddImageBlock(life, g, CodexResourceId.Images_Research_Disk2, idx++);

            AddTextBlock(life, g, T.CodexMResearchDisk3_V1.F(
                Texts.LinkToCodex(DefIdsH.ObjUpgradesDiskCompressionUpgrade)), idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Research_InstallUpgrade_DiskCompression, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMResearchDisk1_V1,
                T.CodexMResearchDisk2_V1,
                T.ObjectsDevicesDiskmodule_V1,
                T.CodexMResearchDisk3_V1,
                T.ObjUpgradesDiskcompressionupgrade);
        }
    }
}
