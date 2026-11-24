using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Minigame2048 : ShipComputerApp_Minigame {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Minigame2048());
        }

        public override string Id => Minigame2048.Game2048Id;
        public override string Name => T.Noun2048;
        public override string Icon => IconId.CGame2048;
        public override string ColorId => "Accent";

        public override void OnClick() {
            A.S.UIQuery.ToggleMinigame.Ask(Id);
        }
    }
}
