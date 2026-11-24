using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class UIFitParentRect : WBase {
        [SerializeField] RectTransform parentRect;
        [SerializeField] private float paddingPx = 0f;
        private float lastW;
        private float lastH;

        private void Awake() {
            if (parentRect == null) {
                parentRect = transform.parent as RectTransform;
            }
        }

        private void Update() {
            var size = parentRect.rect.size;

            // var size = textRect.sizeDelta;
            if (lastW != size.x || lastH != size.y) {
                lastW = size.x;
                lastH = size.y;
                var rekt = parentRect.sizeDelta;
                var min = Mathf.Min(rekt.x, rekt.y);
                min -= paddingPx * 2;
                Rect.sizeDelta = new Vector2(min, min);
            }
        }

        #if UNITY_EDITOR
        [ContextMenu("Rebuild")]
        private void Rebuild() {
            lastH = 0;
            lastW = 0;
            Update();
        }
        #endif
    }
}
