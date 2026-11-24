using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using KL.Utils;
using Game.CodeGen;
using Game.Constants;

namespace Game.UI {
    public class UIClickableHoverable : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler, IPointerClickHandler {
        public Action<bool> OnChange;
        public Action<PointerEventData> OnClick;
        private bool isHovering;
        private bool isShowingFingerCursor;
        private void Start() {
            HoverChanged(false);
        }

        private void HoverChanged(bool show) {
            if (isHovering != show) {
                OnChange?.Invoke(show);
            }
            if (show && !isShowingFingerCursor) {
                isShowingFingerCursor = true;
                The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorFinger);
            } else if (!show && isShowingFingerCursor) {
                isShowingFingerCursor = false;
                The.SysSig.SetCursorOverride.Send(0);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (G.IsClickDrag) { return; }
            if (!isHovering) {
                HoverChanged(true);
                isHovering = true;
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isHovering) {
                HoverChanged(false);
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
                D.Err("OnClick is not set for UIClickableHoverable: {0}",
                    D.GOPath(transform));
            }
        }
    }
}
