using Game.Constants;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenWalletQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenWalletQuickSearchCommand());
        }

        public const string CmdId = "OpenWallet";
        public override string Id => CmdId;
        public override string Icon => IconId.CBriefcase;
        protected override string NameT => T.Wallet;
        public override string DescT => T.Wallet;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleWallet.Ask(true);
            return true;
        }
    }
}
