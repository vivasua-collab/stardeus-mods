using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_MinigameStacker : ShipComputerApp_Minigame {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_MinigameStacker());
        }

        public override string Id => MinigameStacker.StackerId;
        public override string Name => T.NounStacker;
        public override string Icon => IconId.CStackerGame;
        public override string ColorId => "Accent";

        public override void OnClick() {
            A.S.UIQuery.ToggleMinigame.Ask(Id);
        }
    }
}
