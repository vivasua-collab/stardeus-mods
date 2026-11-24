using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Console;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class OpenResearchSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenResearchSearchCommand());
        }
        public const string CmdId = "OpenResearch";
        public override string Id => CmdId;
        public override string Icon => IconId.CResearch;
        public override ActionType Shortcut => ActionType.OpenResearch;
        protected override string NameT => T.OverlayTipResearch;
        public override string DescT => T.OpenResearch;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ShowResearch.Ask(true);
            return true;
        }
    }
}
