using System;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class DragHandler {
        private readonly GameState s;
        private IDraggable currentDraggable;
        private bool isDragging;
        private Vector3 lastMousePos;
        public DragHandler(GameState s) {
            this.s = s;
            s.UIQuery.StartDragging.SetResponder(OnStartDragging);
        }

        private bool OnStartDragging(IDraggable draggable) {
            if (isDragging) {
                if (currentDraggable != null) {
                    D.Err("Already dragging something! {0}", currentDraggable);
                } else {
                    D.Err("Already dragging something!!");
                }
                return false;
            }
            if (draggable.IsDestroyed) {
                D.Err("Can't start dragging a destroyed object! {0}", draggable);
                return false;
            }
            G.IsClickDrag = true;
            currentDraggable = draggable;
            isDragging = true;
            lastMousePos = MouseUtils.ScreenPos;
            // D.Warn("Started dragging {0}", draggable);
            return true;
        }

        public void Update() {
            if (!isDragging) { return; }
            if (The.Bindings.IsPressed(ActionType.Click)) {
                if (currentDraggable.IsDestroyed) {
                    isDragging = false;
                    G.IsClickDrag = false;
                    currentDraggable.OnEndDrag();
                    currentDraggable = null;
                    D.Err("Dragged object was destroyed during drag! Ending drag.");
                    return;
                }
                var mousePos = MouseUtils.ScreenPos;
                Vector2 delta = mousePos - lastMousePos;
                lastMousePos = mousePos;
                currentDraggable.TryDrag(delta);
            } else {
                isDragging = false;
                G.IsClickDrag = false;
                currentDraggable.OnEndDrag();
                currentDraggable = null;
                // D.Warn("Stopped dragging (click up) {0}", currentDraggable);
            }
        }
    }
}
