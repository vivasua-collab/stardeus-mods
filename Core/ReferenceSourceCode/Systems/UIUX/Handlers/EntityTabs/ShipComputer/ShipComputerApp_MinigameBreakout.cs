using Game.Constants;
using Game.Systems.Minigames;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_MinigameBreakout : ShipComputerApp_Minigame {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_MinigameBreakout());
        }

        public override string Id => MinigameBreakout.BreakoutId;
        public override string Name => T.NounBreakout;
        public override string Icon => IconId.CBreakoutGame;
        public override string ColorId => "Accent";

        public override void OnClick() {
            A.S.UIQuery.ToggleMinigame.Ask(Id);
        }
    }
}
