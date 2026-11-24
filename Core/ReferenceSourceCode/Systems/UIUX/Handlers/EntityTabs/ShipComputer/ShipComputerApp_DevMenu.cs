using Game.Constants;
using Game.Input;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_DevMenu : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_DevMenu());
        }

        public override string Id => "DevMenu";
        public override string Name => "Dev Menu";
        public override string Icon => IconId.WDev;
        public override string ColorId => "Red";
        public override bool IsAvailable => WasAvailable || A.InstantBuild || A.S.Scenario.IsSandbox;
        public override bool IsVisible => IsAvailable;
        [ClearOnReload]
        public static bool WasAvailable;
        public override void OnClick() {
            WasAvailable = true;
            A.S.UIQuery.ToggleDevMenu.Ask(true);
        }
    }
}
