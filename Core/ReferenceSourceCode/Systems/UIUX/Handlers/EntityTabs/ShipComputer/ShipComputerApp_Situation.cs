using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Situation : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Situation());
        }

        public override string Id => "Situation";
        public override string Name => T.Situation;
        public override string Icon => IconId.WChart;
        public override string ColorId => "Accent3";
        public override void OnClick() {
            A.S.UIQuery.ToggleSituation.Ask(!UIShowing.Situation);
        }
    }
}
