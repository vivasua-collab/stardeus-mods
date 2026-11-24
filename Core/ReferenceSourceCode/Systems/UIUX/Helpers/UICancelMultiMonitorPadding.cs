using Game.UI;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class CancelMultiMonitorPadding : MonoBehaviour {
        private RectTransform rekt;
        private void OnEnable() {
            var pad = MultiMonitorPadding.UnscaledUIPadding;
            if (pad == 0f) { return; }
            if (rekt == null) {
                rekt = GetComponent<RectTransform>();
            }
            rekt.offsetMin = new Vector2(-pad, 0f);
            rekt.offsetMax = new Vector2(pad, 0f);
        }
    }
}
