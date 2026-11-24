using Game.Constants;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Log : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Log());
        }

        public override string Id => "Log";
        public override string Name => T.OverlayTipEventlog;
        public override string Icon => IconId.WList;
        public override string ColorId => "PanelTop";

        public override void OnClick() {
            A.S.UIQuery.ToggleLog.Ask(!UIShowing.EventLog);
        }
    }
}
