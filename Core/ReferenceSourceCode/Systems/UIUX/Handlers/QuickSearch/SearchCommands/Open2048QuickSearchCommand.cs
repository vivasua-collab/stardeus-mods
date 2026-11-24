using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class Open2048QuickSearchCommand : OpenMinigameQuickSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new Open2048QuickSearchCommand());
        }

        public const string CmdId = "Open2048";
        public override string Id => CmdId;
        public override string Icon => IconId.CGame2048;
        protected override string GameNameT => T.Noun2048;
        public override string MinigameId => Minigame2048.Game2048Id;
    }
}
