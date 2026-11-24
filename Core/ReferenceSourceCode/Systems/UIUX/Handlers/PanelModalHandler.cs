using System;
using Game.Data;
using Game.UI;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class PanelModalHandler : BaseWidgetHandler<PanelModalWidget> {
        private BaseWidget currentHolder;
        public PanelModalHandler(GameState s, PanelModalWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowPanelModalFor.SetResponder(ShowModalFor);
            s.UIQuery.TryClosePanelModal.SetResponder(TryCloseHolder);
        }

        private bool TryCloseHolder() {
            return currentHolder is ICloseableWidget closeable && closeable.TryCloseWidget();
        }

        private bool ShowModalFor(BaseWidget w, bool show) {
            if (currentHolder != null && currentHolder != w) {
                D.Err("Trying to show ({0}) panel modal for {1}, while another holder is active: {2}",
                    show, w, currentHolder);
                return false;
            }
            if (show) {
                currentHolder = w;
                S.Toolbox.Clear();
            } else {
                currentHolder = null;
            }
            UIShowing.Set(UIShowingFlags.PanelModal, show);
            widget.Toggle(show);
            return true;
        }
    }
}
