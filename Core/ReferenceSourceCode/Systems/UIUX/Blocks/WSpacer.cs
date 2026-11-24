using Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WSpacer : WBase {
        public void SetSize(Vector2 size) {
            Rect.sizeDelta = size;
        }
        public void SetWidth(float px) {
            Rect.sizeDelta = new Vector2(px, Rect.sizeDelta.y);
        }
        public void SetHeight(float px) {
            Rect.sizeDelta = new Vector2(Rect.sizeDelta.x, px);
        }
        public override string ToString() {
            return $"WSpacer[{Rect.sizeDelta}]";
        }
    }
}
