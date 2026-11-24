using Game.Constants;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenProductionSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenProductionSearchCommand());
        }

        public const string CmdId = "OpenProduction";
        public override string Id => CmdId;
        public override string Icon => IconId.WProduction;
        public override ActionType Shortcut => ActionType.OpenProduction;
        protected override string NameT => T.NounProductionOverview;
        public override string DescT => T.NounProductionOverview;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleProduction.Ask(true);
            return true;
        }
    }
}
