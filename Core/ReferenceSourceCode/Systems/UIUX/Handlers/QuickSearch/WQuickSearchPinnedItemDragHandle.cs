using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using KL.Pool;
using KL.Utils;
using KL.UweenExt;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Uween;

namespace Game.Systems.UIUX {
    public sealed class WQuickSearchPinnedItemDragHandle : WBase, IDragHandler,
            IPointerDownHandler, IPointerUpHandler,
            IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IEndDragHandler {
        [SerializeField] private WQuickSearchPinnedItem item;
        [SerializeField] private CanvasGroup cg;
        bool isHovering;
        bool isDragging;
        Tween alphaTween;
        public bool IsShowing => cg.alpha > 0f;

        public void OnDrag(PointerEventData eventData) {
            A.S.UIQuery.OnDragQuickSearchPinnedItem.Ask(item, eventData.position);
        }
        public void OnBeginDrag(PointerEventData eventData) {
            isDragging = true;
            G.IsClickDrag = true;
        }

        public void OnEndDrag(PointerEventData eventData) {
            isDragging = false;
            G.IsClickDrag = false;
            cg.alpha = 0.001f;
            The.SysSig.SetCursorOverride.Send(0);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (G.IsClickDrag) { return; }
            if (!isHovering) {
                cg.alpha = 1f;
                isHovering = true;
                The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorHandOpen);
                The.SysSig.ShowTooltip.Send(T.DragToReorder, true);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            if (isHovering) {
                if (!G.IsClickDrag) {
                    cg.alpha = 0.001f;
                }
                isHovering = false;
                if (!isDragging) {
                    The.SysSig.SetCursorOverride.Send(0);
                }
                The.SysSig.HideTooltip.Send();
            }
        }

        private void OnEnable() {
            cg.alpha = 0f;
        }

        private void OnDisable() {
            OnPointerExit(null);
            HideUnlessDragging();
        }

        public void FadeIn() {
            if (cg.alpha == 1f) { return; }
            if (alphaTween != null && !alphaTween.IsComplete) {
                alphaTween.Skip();
            }
            alphaTween = TweenCGA.Add(gameObject, 0.5f, 1f);
        }

        public void HideUnlessDragging() {
            if (isDragging) { return; }
            if (alphaTween != null && !alphaTween.IsComplete) {
                alphaTween.Skip();
            }
            cg.alpha = 0f;
        }

        public void OnPointerDown(PointerEventData eventData) {
            The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorHandClosed);
        }

        public void OnPointerUp(PointerEventData eventData) {
            if (!isDragging) {
                The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorHandOpen);
            }
        }
    }
}
