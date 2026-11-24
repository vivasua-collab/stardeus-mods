using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenStarmapSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenStarmapSearchCommand());
        }
        public const string CmdId = "OpenStarmap";
        public override string Id => CmdId;
        public override string Icon => IconId.CStarmap;
        public override ActionType Shortcut => ActionType.OpenStarmap;
        protected override string NameT => T.Starmap;
        public override string DescT => T.OpenStarmap;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ShowStarmap.Ask(true);
            return true;
        }
    }
}
