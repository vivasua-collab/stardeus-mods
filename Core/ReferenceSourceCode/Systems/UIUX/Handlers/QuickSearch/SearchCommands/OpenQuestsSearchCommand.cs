using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenQuestsSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenQuestsSearchCommand());
        }
        public const string CmdId = "OpenQuests";
        public override string Id => CmdId;
        public override string Icon => IconId.CPickaxe;
        public override ActionType Shortcut => ActionType.OpenQuests;
        protected override string NameT => T.Quests;
        public override string DescT => T.NounAvailableQuests;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ShowQuests.Ask(true);
            return true;
        }
    }
}
