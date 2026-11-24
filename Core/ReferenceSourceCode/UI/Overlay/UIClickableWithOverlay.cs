using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using KL.Utils;
using Game.Constants;

namespace Game.UI {
    public class UIClickableWithOverlay : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler, IPointerClickHandler {
        [SerializeField] private Image overlayImage;

        public Action<bool> OnChange;
        public Action<PointerEventData> OnClick;
        private bool isHovering;

        private void Start() {
            ShowOverlay(false);
        }

        private void ShowOverlay(bool show) {
            overlayImage.enabled = show;
            if (isHovering != show) {
                OnChange?.Invoke(show);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (G.IsClickDrag) { return; }
            if (!isHovering) {
                ShowOverlay(true);
                isHovering = true;
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isHovering) {
                ShowOverlay(false);
                isHovering = false;
            }
        }

        private void OnDisable() {
            OnPointerExit(null);
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (OnClick != null) {
                OnClick(eventData);
            } else {
                D.Err("OnClick is not set for UIClickableWithOverlay: {0}",
                    D.GOPath(transform));
            }
        }
    }
}
