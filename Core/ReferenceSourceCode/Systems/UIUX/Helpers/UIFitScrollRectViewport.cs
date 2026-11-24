using KL.Utils;
using Unity.XR.OpenVR;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class UIFitScrollRectViewport : WBase {
        [SerializeField] RectTransform viewport;
        [SerializeField] private float padding;
        [SerializeField] private float maxHeight;
        private float lastH;

        private void Awake() {
            if (viewport == null) {
                viewport = GetComponentInParent<ScrollRect>().viewport;
            }
        }

        private void Update() {
            var size = viewport.rect.size;

            // var size = textRect.sizeDelta;
            if (lastH != size.y) {
                lastH = size.y;
                if (maxHeight > 0f && lastH > maxHeight) {
                    lastH = maxHeight;
                }
                Rect.sizeDelta = new Vector2(Rect.sizeDelta.x, lastH - padding);
            }
        }

        #if UNITY_EDITOR
        [ContextMenu("Rebuild")]
        private void Rebuild() {
            lastH = 0;
            Update();
        }
        #endif
    }
}
