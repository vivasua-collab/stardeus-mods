using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class OpenStackerQuickSearchCommand : OpenMinigameQuickSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenStackerQuickSearchCommand());
        }

        public const string CmdId = "OpenStacker";
        public override string Id => CmdId;
        public override string Icon => IconId.CStackerGame;
        protected override string GameNameT => T.NounStacker;
        public override string MinigameId => MinigameStacker.StackerId;
    }
}
