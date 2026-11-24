using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Terraforming : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Terraforming());
        }

        public override string Id => "Terraforming";
        public override string Name => T.Terraforming;
        public override string Icon => IconId.CTerraform;
        public override string ColorId => "GreenDark";
        // Empty until there is a planet to terraform
        public override bool IsVisible {
            get {
                var t = A.S.Sys.Terraform;
                return t.Target != null || t.KnownTerraformable.Count > 0;
            }
        }

        public override void OnClick() {
            A.S.UIQuery.ToggleTerraformConfig.Ask(!UIShowing.Terraforming);
        }
    }
}
