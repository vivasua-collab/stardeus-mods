using System;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenExpeditionsSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenExpeditionsSearchCommand());
        }
        public const string CmdId = "OpenExpeditions";
        public override string Id => CmdId;
        public override string Icon => IconId.CPickaxe;
        protected override string NameT => T.Expeditions;
        public override string DescT => T.CurrentExpeditions;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ShowExpeditions.Ask(true);
            return true;
        }
    }
}
