using Game.Data;
using Game.UI;

namespace Game.Systems.UIUX {
    public sealed class ShipConfigHandler : BaseWidgetHandler<ShipConfigWidget> {
        private readonly ShipConfigContext ctx;
        public ShipConfigHandler(GameState s, ShipConfigWidget widget) : base(s, widget) {
            ctx = new ShipConfigContext();
            widget.Initialize(s, ctx);
            s.UIQuery.ToggleShipConfig.SetResponder(ToggleShipConfig);
            s.UIQuery.RebuildShipConfig.SetResponder(RebuildShipConfig);
        }

        private bool RebuildShipConfig() {
            if (UIShowing.ShipConfig) {
                widget.Load();
                return true;
            }
            return false;
        }

        private bool ToggleShipConfig(bool show) {
            widget.Toggle(show);
            return true;
        }
    }
}
