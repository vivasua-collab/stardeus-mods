using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenLogQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenLogQuickSearchCommand());
        }

        public const string CmdId = "OpenLog";
        public override string Id => CmdId;
        public override string Icon => IconId.WList;
        protected override string NameT => T.OverlayTipEventlog;
        public override string DescT => T.OverlayTipEventlog;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleLog.Ask(true);
            return true;
        }
    }
}
