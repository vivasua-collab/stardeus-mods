using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenPoliciesSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenPoliciesSearchCommand());
        }

        public const string CmdId = "OpenPolicies";
        public override string Id => CmdId;
        public override string Icon => IconId.CPoliceBadge;
        protected override string NameT => T.Policies;
        public override string DescT => T.PoliciesDesc;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.TogglePolicies.Ask(true);
            return true;
        }
    }
}
