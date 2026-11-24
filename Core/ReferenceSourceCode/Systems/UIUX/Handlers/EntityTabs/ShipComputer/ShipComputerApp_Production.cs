using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Production : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Production());
        }

        public override string Id => "Production";
        public override string Name => T.MenuProduction;
        public override string Icon => IconId.WProduction;
        public override string ColorId => "Black";
        public override ActionType Shortcut => ActionType.OpenProduction;
        public override void OnClick() {
            A.S.UIQuery.ToggleProduction.Ask(!UIShowing.Production);
        }
    }
}
