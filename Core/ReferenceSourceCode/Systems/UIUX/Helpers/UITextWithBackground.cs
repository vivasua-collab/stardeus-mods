using TMPro;
using UnityEngine;

namespace Game.Systems.UIUX {

    public class UITextWithBackground : MonoBehaviour {
        private TMP_Text text;
        [SerializeField] RectTransform backgroundRect;
        [SerializeField] private bool useTextRect;
        private RectTransform textRect;
        private float lastW;
        private float lastH;
        private void Awake() {
            text = GetComponent<TMP_Text>();
            if (useTextRect) {
                textRect = GetComponent<RectTransform>();
            }
        }
        private void LateUpdate() {
            Vector2 s;
            if (useTextRect) {
                s = textRect.sizeDelta;
            } else {
                s = text.textBounds.size;
                s = new Vector2(s.x + 8, s.y);
            }

            if (lastW != s.x || lastH != s.y) {
                lastW = s.x;
                lastH = s.y;
                backgroundRect.sizeDelta = new Vector2(s.x, s.y);
            }
        }

        #if UNITY_EDITOR
        [ContextMenu("Rebuild")]
        private void Rebuild() {
            lastW = 0;
            lastH = 0;
            Awake();
            LateUpdate();
        }
        #endif
    }
}
