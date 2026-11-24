using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_MinigameSnake : ShipComputerApp_Minigame {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_MinigameSnake());
        }

        public override string Id => MinigameSnake.SnakeId;
        public override string Name => T.NounSnake;
        public override string Icon => IconId.CSnakeGame;
        public override string ColorId => "Accent";

        public override void OnClick() {
            A.S.UIQuery.ToggleMinigame.Ask(Id);
        }
    }
}
