using System;
using Game.Data;

namespace Game.Systems.UIUX {
    public sealed class PanelContainerHandler : BaseWidgetHandler<PanelContainerWidget> {
        public PanelContainerHandler(GameState s, PanelContainerWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.TryCloseTopmostPanel.SetResponder(TryCloseTopmost);
            s.UIQuery.TryFocusSearchField.SetResponder(TryFocusSearchField);
        }

        private bool TryFocusSearchField() {
            return widget.TryFocusSearchField();
        }

        private bool TryCloseTopmost() {
            return widget.TryCloseTopmost();
        }
    }
}
