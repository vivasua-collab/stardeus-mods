using Game.Constants;

namespace Game.Systems.UIUX {
    public sealed class PanelModalWidget : BaseWidget {
        public void Initialize() {
            Register(UIWidgetId.PanelModal, this);
            Toggle(false);
        }
    }
}
