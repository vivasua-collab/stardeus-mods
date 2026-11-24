using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class OpenSnakeQuickSearchCommand : OpenMinigameQuickSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenSnakeQuickSearchCommand());
        }

        public const string CmdId = "OpenSnake";
        public override string Id => CmdId;
        public override string Icon => IconId.CSnakeGame;
        protected override string GameNameT => T.NounSnake;
        public override string MinigameId => MinigameSnake.SnakeId;
    }
}
