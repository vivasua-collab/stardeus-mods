using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX.Codex.Pages {
    public sealed class ComputerResourcesBonusPage : CodexConceptPage {
        public const string PageId = "Electricity/ComputerResourcesBonus";
        public override string Id => PageId;
        public override string TitleT => T.CodexMElectricityComputerresourcesbonus;
        protected override string GroupTitleT => T.CodexMElectricity;

        public override void Build(GameState s, Lifetime life, WGroup g, int idx) {
            AddTextBlock(life, g, T.CodexMElectricityComputerresourcesbonusDesc, idx++);
            AddVideoBlock(life, g, CodexResourceId.Videos_Electricity_CPUBonus, idx++);
        }

        public override bool OnSearchPageBody(string query) {
            return QueryMatches(query, T.CodexMElectricityComputerresourcesbonusDesc,
                T.CpuBonus, T.ComputerResourcesCpu);
        }
    }
}
