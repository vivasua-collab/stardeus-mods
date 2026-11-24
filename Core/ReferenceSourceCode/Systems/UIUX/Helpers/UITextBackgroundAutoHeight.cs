using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {

    public class UITextBackgroundAutoHeight : MonoBehaviour {
        private TMP_Text text;
        [SerializeField] RectTransform backgroundRect;
        [SerializeField] float padding;
        private float lastH;

        private void Awake() {
            text = GetComponent<TMP_Text>();
        }
        private void LateUpdate() {
            var s = text.textBounds.size;

            // var size = textRect.sizeDelta;
            if (lastH != s.y) {
                lastH = s.y;
                var rekt = backgroundRect.sizeDelta;
                backgroundRect.sizeDelta = new Vector2(rekt.x, lastH + padding);
            }
        }

        #if UNITY_EDITOR
        [ContextMenu("Rebuild")]
        private void Rebuild() {
            lastH = 0;
            Awake();
            LateUpdate();
        }
        #endif
    }
}
