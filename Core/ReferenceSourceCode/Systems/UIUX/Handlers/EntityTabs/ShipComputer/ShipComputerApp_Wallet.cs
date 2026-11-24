using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Wallet : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Wallet());
        }

        public override string Id => "Wallet";
        public override string Name => T.Wallet;
        public override string Icon => IconId.CBriefcase;
        public override string ColorId => "Grey";
        public override void OnClick() {
            A.S.UIQuery.ToggleWallet.Ask(!UIShowing.Wallet);
        }
    }
}
