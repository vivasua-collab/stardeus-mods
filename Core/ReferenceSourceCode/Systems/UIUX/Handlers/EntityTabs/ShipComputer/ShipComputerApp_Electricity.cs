using Game.Constants;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Electricity : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Electricity());
        }

        public override string Id => "Electricity";
        public override string Name => T.ElectricityGrid;
        public override string Icon => IconId.CEnergy;
        public override string ColorId => "Blue";
        public override string HotkeyDefault => "<Keyboard>/g";
        public override void OnClick() {
            A.S.UIQuery.ToggleElectricity.Ask(!UIShowing.Electricity);
        }
    }
}
