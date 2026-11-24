using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Game.CodeGen;
using Game.Constants;

namespace Game.UI {
    public class UIButtonWithHoverCursor : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler {
        [SerializeField] private Selectable selectable;
        private bool isShowingFingerCursor;
        public Action<bool> OnHover;
        private void Awake() {
            if (selectable == null) {
                selectable = GetComponent<Selectable>();
            }
        }
        public void OnPointerEnter(PointerEventData eventData) {
            if (!isShowingFingerCursor) {
                if (selectable?.interactable == false) { return; }
                OnHover?.Invoke(true);
                if (G.IsClickDrag) { return; }
                isShowingFingerCursor = true;
                The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorFinger);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isShowingFingerCursor) {
                isShowingFingerCursor = false;
                OnHover?.Invoke(false);
                The.SysSig.SetCursorOverride.Send(0);
            }
        }

        private void OnDisable() {
            OnPointerExit(null);
            // if (isShowingFingerCursor) {
            //     isShowingFingerCursor = false;
            //     The.SysSig.SetCursorOverride.Send(0);
            // }
        }
    }
}
