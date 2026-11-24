using Game.Constants;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Codex : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Codex());
        }

        public override string Id => "Codex";
        public override string Name => T.Codex;
        public override string Icon => IconId.CInfo;
        public override string ColorId => "White";
        public override void OnClick() {
            A.S.UIQuery.ToggleCodex.Ask(!UIShowing.Codex);
        }
    }
}
