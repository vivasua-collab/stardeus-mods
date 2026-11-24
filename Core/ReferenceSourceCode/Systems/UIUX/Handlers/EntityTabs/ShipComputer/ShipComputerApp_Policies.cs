using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Policies : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Policies());
        }

        public override string Id => "Policies";
        public override string Name => T.Policies;
        public override string Icon => IconId.CPoliceBadge;
        public override string ColorId => "Accent2";
        public override void OnClick() {
            A.S.UIQuery.TogglePolicies.Ask(!UIShowing.Policies);
        }
    }
}
