using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenConsoleSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenConsoleSearchCommand());
        }
        public const string CmdId = "OpenConsole";
        public override string Id => CmdId;
        public override string Icon => IconId.CCog;
        public override ActionType Shortcut => ActionType.ToggleConsole;
        protected override string NameT => "In-Game Console";
        public override string DescT => "Open the in-game console";
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            InGameConsole.ForceShow();
            return true;
        }
    }
}
