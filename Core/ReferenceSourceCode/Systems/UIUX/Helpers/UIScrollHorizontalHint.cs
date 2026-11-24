using KL.Utils;
using KL.UweenExt;
using UnityEngine;
using UnityEngine.EventSystems;
using Uween;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class UIScrollHorizontalHint : MonoBehaviour,
            IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private GameObject[] scrollHints;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        private bool isShowing;

        private void Awake() {
            DeactivateScrollHint();
        }

        private bool IsScrollNeeded() {
            var contentWidth = content.rect.width;
            var viewportWidth = viewport.rect.width;
            return contentWidth > viewportWidth;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (!IsScrollNeeded()) return;
            isShowing = true;
            foreach (var scrollHint in scrollHints) {
                scrollHint.SetActiveFast(true);
                TweenCGA.Add(scrollHint, 0.25f, 0.85f).Delay(0.5f);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (!isShowing && !IsScrollNeeded()) return;
            isShowing = false;
            foreach (var scrollHint in scrollHints) {
                TweenCGA.Add(scrollHint, 0.25f, 0f).Then(DeactivateScrollHint);
            }
        }

        private void DeactivateScrollHint() {
            isShowing = false;
            foreach (var scrollHint in scrollHints) {
                scrollHint.SetActiveFast(false);
            }
        }
    }
}
