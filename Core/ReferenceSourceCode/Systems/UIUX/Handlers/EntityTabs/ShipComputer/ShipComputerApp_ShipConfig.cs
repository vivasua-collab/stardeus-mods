using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_ShipConfig : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_ShipConfig());
        }

        public const string AppId = "ShipConfig";

        public override string Id => AppId;
        public override string Name => T.ShipConfigShort;
        public override string Icon => IconId.CSpaceShip;
        public override string ColorId => "Blue";
        public override void OnClick() {
            A.S.UIQuery.ToggleShipConfig.Ask(!UIShowing.ShipConfig);
        }
    }
}
