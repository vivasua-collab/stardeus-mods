using UnityEngine;
using UnityEngine.UI;

namespace Game.UI {
    /// <summary>
    /// Handles the little animated arrow that shows up when you hover
    /// an item that has a submenu in the Info Panel
    /// </summary>
    public sealed class SubmenuMarker : MonoBehaviour {
        [SerializeField] private Image markerImage;
        [SerializeField] private RectTransform rekt;
        private bool isVisible;
        private void Awake() {
            isVisible = true;
            Hide();
        }
        public void Hide() {
            if (isVisible) {
                gameObject.SetActive(false);
                isVisible = false;
            }
        }

        public void ShowFor(Vector2 offset, RectTransform rt) {
            // TODO move properly
            var p = rt.anchoredPosition;
            var d = rt.sizeDelta;
            rekt.anchoredPosition = offset + new Vector2(p.x + d.x, p.y);
            //rekt.SetAsLastSibling();

            if (!isVisible) {
                //markerImage.enabled = true;
                gameObject.SetActive(true);
                isVisible = true;
            }
        }
    }
}
