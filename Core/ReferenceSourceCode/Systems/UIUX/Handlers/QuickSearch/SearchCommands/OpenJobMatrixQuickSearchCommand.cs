using Game.Constants;
using Game.Systems.Copters;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenJobMatrixQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenJobMatrixQuickSearchCommand());
        }

        public const string CmdId = "OpenJobMatrix";
        public override string Id => CmdId;
        public override string Icon => IconId.CWrench2;
        protected override string NameT => T.NounJobs;
        public override string DescT => T.NounJobs;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleJobMatrix.Ask(true);
            return true;
        }
    }
}
