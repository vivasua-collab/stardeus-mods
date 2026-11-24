using Game.Constants;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_CombatControls : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_CombatControls());
        }

        public override string Id => "CombatControls";
        public override string Name => T.CombatControls;
        public override string Icon => IconId.CCombat;
        public override string ColorId => "Red";
        public override void OnClick() {
            A.S.UIQuery.ToggleCombatControls.Ask(!UIShowing.CombatControls);
        }
    }
}
