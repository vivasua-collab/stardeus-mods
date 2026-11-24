using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Surgeries : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Surgeries());
        }

        public override string Id => "Surgeries";
        public override string Name => T.Health;
        public override string Icon => IconId.CHealth;
        public override string ColorId => "GreenDark";
        public override ActionType Shortcut => ActionType.OpenSurgeries;
        public override void OnClick() {
            A.S.UIQuery.ToggleSurgeries.Ask(!UIShowing.Surgeries);
        }
    }
}
