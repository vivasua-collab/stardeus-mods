using Game.Components;
using Game.Constants;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Expeditions : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Expeditions());
        }

        public override string Id => "Expeditions";
        public override string Name => T.Expeditions;
        public override string Icon => IconId.CPickaxe;
        public override string ColorId => "PanelFooter";
        // Nothing to send without a shuttle; one away on an expedition still counts
        public override bool IsVisible {
            get {
                var s = A.S;
                return s.Sys.Expedition.Expeditions.Count > 0
                    || s.Components.CountMatching(ShuttleComp.IsUsable, max: 1) > 0;
            }
        }

        public override void OnClick() {
            A.S.UIQuery.ShowExpeditions.Ask(!UIShowing.Expeditions);
        }
    }
}
