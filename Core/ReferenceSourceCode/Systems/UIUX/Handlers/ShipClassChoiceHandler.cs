using System;
using System.Collections.Generic;
using Game.Data;
using Game.Systems.Ships;

namespace Game.Systems.UIUX {
    public sealed class ShipClassChoiceHandler : BaseWidgetHandler<ShipClassChoiceWidget> {
        public ShipClassChoiceHandler(GameState s, ShipClassChoiceWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowShipClassChoice.SetResponder(ShowChoice);
            s.UIQuery.ForceShowShipClassChoice.SetResponder(ForceShowChoice);
        }

        private bool ShowChoice(List<ShipClassDef> options, Action<ShipClassDef> onChoose) {
            return widget.Show(S, options, onChoose);
        }

        private bool ForceShowChoice(List<ShipClassDef> options, Action<ShipClassDef> onChoose) {
            return widget.Show(S, options, onChoose, canCancel: false);
        }
    }
}
