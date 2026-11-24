using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Game.CodeGen;
using KL.Utils;
using Game.Constants;

namespace Game.UI {
    public class UIButtonWithOverlay : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler {
        [SerializeField] private Image overlayImage;
        [SerializeField] private Selectable selectable;
        public bool AllowOnHoverWhenDisabled;


        public Action<bool> OnHover;
        public bool IsHovering;
        private bool isShowingFingerCursor;
        private bool alwaysShow;

        public void ForceOverlay(bool show) {
            overlayImage.enabled = show;
        }

        private void Start() {
            #if UNITY_EDITOR
            if (overlayImage == null) {
                D.Err("OverlayImage is null for UIButtonWithOverlay. You likely wanted UIButtonWithHoverCursor! {0}", D.GOPath(transform));
                // Destroy(gameObject);
                return;
            }
            #endif
            selectable = GetComponentInParent<Selectable>();
            ShowOverlay(false, false);
        }

        private void ShowOverlay(bool show, bool isHovering) {
            if (isHovering && !isShowingFingerCursor) {
                isShowingFingerCursor = true;
                The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorFinger);
            } else if (!isHovering && isShowingFingerCursor) {
                isShowingFingerCursor = false;
                The.SysSig.SetCursorOverride.Send(0);
            }

            if (!show && alwaysShow) {
                return;
            }
            if (show && !AllowOnHoverWhenDisabled && selectable?.interactable == false) {
                show = false;
            }
            if (IsHovering != show) {
                OnHover?.Invoke(show);
            }
            overlayImage.enabled = show;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            ShowOverlay(true, !G.IsClickDrag);
            IsHovering = true;
        }

        public void OnPointerExit(PointerEventData eventData) {
            ShowOverlay(false, false);
            IsHovering = false;
        }

        private void OnDisable() {
            OnPointerExit(null);
        }

        public void AlwaysShow(bool yes) {
            alwaysShow = yes;
        }
    }
}
