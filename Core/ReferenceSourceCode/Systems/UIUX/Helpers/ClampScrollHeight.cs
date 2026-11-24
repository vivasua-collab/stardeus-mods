using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class ClampScrollHeight : MonoBehaviour {
        [SerializeField] public RectTransform content;
        [SerializeField] private LayoutElement layoutElement;
        public float maxHeight = 300f;
        private float lastHeight;

        [ContextMenu("Calculate")]
        private void LateUpdate() {
            float h = content.rect.height;
            if (lastHeight != h) {
                layoutElement.preferredHeight = Mathf.Min(h, maxHeight);
                lastHeight = h;
            }
        }
    }
}
