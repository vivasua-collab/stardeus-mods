using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Notes : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Notes());
        }

        public override string Id => "Notes";
        public override string Name => T.OverlayTipNotes;
        public override string Icon => IconId.WPencil;
        public override string ColorId => "Accent";
        public override void OnClick() {
            A.S.UIQuery.ToggleNotes.Ask(!UIShowing.Notes);
        }
    }
}
