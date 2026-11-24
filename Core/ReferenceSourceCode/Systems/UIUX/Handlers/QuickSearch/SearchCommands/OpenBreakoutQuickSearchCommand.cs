using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class OpenBreakoutQuickSearchCommand : OpenMinigameQuickSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenBreakoutQuickSearchCommand());
        }

        public const string CmdId = "OpenBreakout";
        public override string Id => CmdId;
        public override string Icon => IconId.CBreakoutGame;
        protected override string GameNameT => T.NounBreakout;
        public override string MinigameId => MinigameBreakout.BreakoutId;
    }
}
