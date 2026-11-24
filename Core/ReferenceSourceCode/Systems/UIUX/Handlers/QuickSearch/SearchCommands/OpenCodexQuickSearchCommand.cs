using Game.Constants;
using Game.Systems.Copters;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenCodexQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenCodexQuickSearchCommand());
        }

        public const string CmdId = "OpenCodex";
        public override string Id => CmdId;
        public override string Icon => IconId.CInfo;
        protected override string NameT => T.Codex;
        public override string DescT => T.Codex;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleCodex.Ask(true);
            return true;
        }
    }
}
