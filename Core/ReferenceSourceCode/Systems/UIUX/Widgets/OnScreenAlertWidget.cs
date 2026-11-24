using Game.Constants;
using Game.UI;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class OnScreenAlertWidget : BaseWidget {
        public FanfareMessage FanfareMessage;
        public FanfareMessage ErrorMessage;
        public void Initialize() {
            Register(UIWidgetId.OnScreenAlerts, this);
        }
    }
}
