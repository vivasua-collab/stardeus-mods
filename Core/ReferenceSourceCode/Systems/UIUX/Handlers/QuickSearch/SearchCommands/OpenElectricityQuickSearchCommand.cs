using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenElectricityQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenElectricityQuickSearchCommand());
        }

        public const string CmdId = "OpenElectricity";
        public override string Id => CmdId;
        public override string Icon => IconId.CEnergy;
        protected override string NameT => T.ElectricityGrid;
        public override string DescT => T.ElectricityGrid;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleElectricity.Ask(!UIShowing.Electricity);
            return true;
        }
    }
}
