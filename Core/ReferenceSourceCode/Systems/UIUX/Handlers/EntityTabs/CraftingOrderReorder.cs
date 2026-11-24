using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Crafting;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    // Drives click+drag reordering of crafting order rows in ProductionTab.
    // Reorders crafter.Orders in place - that list is the same instance CraftingSys
    // uses for cycle priority and save/load, so no extra persistence is needed.
    public sealed class CraftingOrderReorder {
        private readonly MultiCrafterComp crafter;
        private readonly RectTransform container;
        private readonly List<WBase> widgets = new();
        private WBase dragging;
        private int baseOffset;

        public CraftingOrderReorder(MultiCrafterComp crafter, RectTransform container) {
            this.crafter = crafter;
            this.container = container;
        }

        public void Register(WBase ow, UIDragListener listener) {
            widgets.Add(ow);
            listener.OnDragBegin = _ => OnBegin(ow);
            listener.OnDragOngoing = OnDrag;
            listener.OnDragEnd = _ => OnEnd();
        }

        private void OnBegin(WBase ow) {
            dragging = ow;
            baseOffset = widgets[0].Rect.GetSiblingIndex();
            G.IsClickDrag = true;
            The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorHandClosed);
        }

        private void OnDrag(PointerEventData e) {
            if (dragging == null) { return; }
            var dragIdx = widgets.IndexOf(dragging);
            var targetIdx = FindIndexAtY(e.position.y);
            if (dragIdx < 0 || targetIdx < 0 || targetIdx == dragIdx) { return; }

            widgets.RemoveAt(dragIdx);
            widgets.Insert(targetIdx, dragging);

            var orders = crafter.Orders;
            var order = orders[dragIdx];
            orders.RemoveAt(dragIdx);
            orders.Insert(targetIdx, order);

            for (int i = 0; i < widgets.Count; i++) {
                widgets[i].SetSiblingIdx(baseOffset + i);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        }

        private int FindIndexAtY(float y) {
            for (int i = 0; i < widgets.Count; i++) {
                var rect = widgets[i].Rect;
                rect.GetScreenBottom(out var bl, out var _);
                rect.GetScreenTop(out var tl, out var _);
                if (y >= bl.y && y <= tl.y) {
                    return i;
                }
            }
            return -1;
        }

        private void OnEnd() {
            dragging = null;
            G.IsClickDrag = false;
            // because we are still hovering
            The.SysSig.SetCursorOverride.Send(CursorTypeH.CursorFinger);
            crafter.UpdateExtraInfo();
        }
    }
}
