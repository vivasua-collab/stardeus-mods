using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenSituationQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenSituationQuickSearchCommand());
        }

        public const string CmdId = "OpenSituation";
        public override string Id => CmdId;
        public override string Icon => IconId.WChart;
        protected override string NameT => T.Situation;
        public override string DescT => T.Situation;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleSituation.Ask(true);
            return true;
        }
    }
}
