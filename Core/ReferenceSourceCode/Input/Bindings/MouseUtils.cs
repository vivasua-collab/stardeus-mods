using UnityEngine.EventSystems;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using KL.UI;
using static UnityEngine.EventSystems.PointerEventData;
using KL.Utils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.Rendering;

namespace Game.Input {
    public static class MouseUtils {
        public static Mouse CurrentMouse = Mouse.current;
        private const float TopBarHeight = 48f;

        public static void SetCurrentMouse(Mouse mouse) {
            if (mouse == CurrentMouse) { return; }
            D.Warn("Mouse switched: {0} -> {1}", CurrentMouse, mouse);
            CurrentMouse = mouse;
            The.SysSig.MouseSwitched.Send();
            //mouse.MakeCurrent();
        }
        private static bool loggedNoMouseError;

        public static Camera WorldCamera;
        public static Vector3 ScreenPos {
            get {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    if (CurrentMouse == null) { return Vector3.zero; }
                }
                #endif
                if (CurrentMouse == null) {
                    SetCurrentMouse(Mouse.current);
                    //CurrentMouse = Mouse.current;
                    if (CurrentMouse == null) {
                        if (!loggedNoMouseError) {
                            loggedNoMouseError = true;
                            D.Err("CurrentMouse and Mouse.current are both null!");
                        }
                        return Consts.V2Zero;
                    }
                }
                var pos = CurrentMouse.position.ReadValue();
                return new Vector2(
                    Mathf.Clamp(pos.x, 0, Screen.width),
                    Mathf.Clamp(pos.y, 0, Screen.height)
                );
            }
        }
        public static Vector3 ScreenPosUnclamped {
            get {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    if (CurrentMouse == null) { return Vector3.zero; }
                }
                #endif
                return CurrentMouse.position.ReadValue();
            }
        }
        public static Vector3 WorldPositionUncached =>
            WorldCamera.ScreenToWorldPoint(ScreenPos);
        public static Vector3 WorldPositionWithOffset(Vector2 off) {
            return WorldCamera.ScreenToWorldPoint(ScreenPos + (Vector3) off);
        }
        public static bool IsOverUI {
            get {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    if (CurrentMouse == null) { return false; }
                }
                #endif
                return EventSystem.current.IsPointerOverGameObject();
            }
        }

        public static int CustomUILastHoverFrame;

        public static readonly List<RaycastResult> RaycastRes = new();

        private static int lastScrollRaycastFrame = -1;
        private static int lastUnderCursorFrame = -1;
        private static bool isOverScrollRect;
        private static GameObject underCursor;

        private static int raycastCacheFrame;
        private static Vector2 raycastCachePos;

        public static void CachedRaycast(Vector2 pos) {
            var fc = Time.frameCount;
            if (fc == raycastCacheFrame && raycastCachePos == pos) {
                return;
            }
            var ev = EventSystem.current;
            var pd = new PointerEventData(ev);
            pd.position = pos;
            raycastCacheFrame = fc;
            raycastCachePos = pos;
            ev.RaycastAll(pd, RaycastRes);
        }

        private static InputSystemUIInputModule uiInputModule;

        // InputSystemUIInputModule already raycasts the mouse pointer every frame
        // in EventSystem.Update and keeps the hit in pointerCurrentRaycast. Reading
        // that instead of running our own RaycastAll removes a full UI raycast per
        // frame (and the PointerEventData garbage that came with it).
        // See Docs/Architecture/UIRaycastReuse.md
        public static GameObject FirstUnderCursor {
            get {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    if (CurrentMouse == null) { return null; }
                }
                #endif
                var fc = Time.frameCount;
                if (fc == lastUnderCursorFrame) { return underCursor; }
                lastUnderCursorFrame = fc;
                var ev = EventSystem.current;
                var module = ev.currentInputModule;
                if (uiInputModule == null || uiInputModule != module) {
                    uiInputModule = module as InputSystemUIInputModule;
                }
                if (uiInputModule == null) {
                    // Should not happen, EventSystem prefab pins the module type
                    CachedRaycast(CurrentMouse.position.ReadValue());
                    if (!Arrays.IsEmpty(RaycastRes)) {
                        underCursor = RaycastRes[0].gameObject;
                    } else {
                        underCursor = null;
                    }
                    return underCursor;
                }
                // In single mouse mode the module tags its one pointer with the id of
                // whichever Mouse device (sysMouse or gamepad vMouse) last produced
                // state, while CurrentMouse is switched by VirtualMouseInput on its
                // own schedule. Try both ids so transition frames don't lose the hit.
                var hit = uiInputModule.GetLastRaycastResult(CurrentMouse.deviceId);
                if (!hit.isValid) {
                    var sysMouse = Mouse.current;
                    if (sysMouse != null && sysMouse != CurrentMouse) {
                        hit = uiInputModule.GetLastRaycastResult(sysMouse.deviceId);
                    }
                }
                underCursor = hit.gameObject;
                return underCursor;
            }
        }

        public static bool IsOverScrollableUI {
            get {
                var fc = Time.frameCount;
                if (fc == lastScrollRaycastFrame) { return isOverScrollRect; }
                lastScrollRaycastFrame = fc;
                CachedRaycast(CurrentMouse.position.ReadValue());
                for (int i = 0; i < RaycastRes.Count; i++) {
                    RaycastResult r = RaycastRes[i];
                    var go = r.gameObject;
                    if (go.GetComponentInParent<ScrollRect>() != null ||
                            go.GetComponentInChildren<ScrollRect>() != null) {
                        D.Log("Raycast over scrollable: {0}", go.name);
                        isOverScrollRect = true;
                        return isOverScrollRect;
                    }
                }
                isOverScrollRect = false;
                return false;
            }
        }
        public static bool IsHovering(Vector2 mousePos, RectTransform ob) {
            if (ob == null) { return false; }
            var go = ob.gameObject;
            if (!go.activeInHierarchy) { return false; }
            CachedRaycast(mousePos);
            for (int i = 0; i < RaycastRes.Count; i++) {
                RaycastResult r = RaycastRes[i];
                if (r.gameObject == ob) {
                    return true;
                }
            }
            return false;
        }

        public static bool IsUIAt(Vector2 screenPos) {
            var ev = EventSystem.current;
            var pd = new PointerEventData(ev);
            pd.position = screenPos;
            ev.RaycastAll(pd, RaycastRes);
            return RaycastRes.Count > 0;
        }

        public static float Slack => 0.01f * WorldCamera.orthographicSize;
        internal static bool IsOverGameView {
            get {
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning) {
                    if (CurrentMouse == null) { return true; }
                }
                #endif
                return IsPositionOverGameView(
                    CurrentMouse.position.ReadValue());
            }
        }
        public static bool IsPositionOverGameView(Vector2 pos, RectOffset padding, float uiScale) {
            return !(pos.x < padding.left * uiScale || pos.y < padding.bottom * uiScale ||
                pos.x > RenderUtils.ScreenWidth - padding.right * uiScale ||
                pos.y > RenderUtils.ScreenHeight - padding.top * uiScale);
        }

        public static bool IsPositionOverGameView(Vector2 pos, float yPad = 0f) {
            return !(pos.x < 0 || pos.y < 0 ||
                pos.x > Screen.width ||
                pos.y > Screen.height - yPad);
        }

        private readonly static Vector3[] corners = new Vector3[4];

        public static void HandleUIDrag(RectTransform parent,
                IList<RectTransform> elements, PointerEventData evData) {
            var btn = evData.button;
            if (btn != InputButton.Left) { return; }
            if (evData.selectedObject != null) {
                // We are still holding a pointer pressed down on a button
                if (Vectors.SqrDistance(evData.pressPosition, evData.position) < 400) {
                    return;
                }
                /*
                // no good because you can move the mouse just a bit after holding the button for a while
                if (evData.clickTime > GameTimer.unscaledTime - 0.2f) { return; }
                // this is no good because overlay can have no block raycast but will get pointer enter
                if (evData.pointerEnter == evData.selectedObject) { return; }
                */
            }
            var d = evData.delta;
            int xmul = 1;
            int ymul = 1;
            var dx = new Vector2(d.x, 0);
            var dy = new Vector2(0, d.y);
            var minX = float.MaxValue;
            var minY = float.MaxValue;
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            for (int i = 0; i < elements.Count; i++) {
                RectTransform rekt = elements[i];
                if (xmul == 0 && ymul == 0) { break; }
                if (rekt == null) { continue; }
                if (rekt == parent) { continue; }
                if (!rekt.gameObject.activeInHierarchy) { continue; }
                rekt.GetWorldCorners(corners);
                for (int i1 = 0; i1 < corners.Length; i1++) {
                    Vector3 c = corners[i1];
                    if (c.x > maxX) {
                        maxX = c.x;
                    }
                    if (c.y > maxY) {
                        maxY = c.y;
                    }
                    if (c.x < minX) {
                        minX = c.x;
                    }
                    if (c.y < minY) {
                        minY = c.y;
                    }
                }
            }

            Vector2 bl = new Vector2(minX, minY);
            Vector2 tr = new Vector2(maxX, maxY);

            var isOver = IsPositionOverGameView(bl);
            if (isOver) {
                if (xmul == 1 && !IsPositionOverGameView(bl + dx)) {
                    xmul = 0;
                }
                if (ymul == 1 && !IsPositionOverGameView(bl + dy)) {
                    ymul = 0;
                }
            }
            if (xmul != 0 || ymul != 0) {
                isOver = IsPositionOverGameView(tr);
                if (isOver) {
                    if (xmul == 1 && !IsPositionOverGameView(tr + dx)) {
                        xmul = 0;
                    }
                    if (ymul == 1 && !IsPositionOverGameView(tr + dy)) {
                        ymul = 0;
                    }
                }
            }

            if (xmul != 0 || ymul != 0) {
                parent.localPosition += new Vector3(d.x * xmul, d.y * ymul, 0);
            }
            // We are clicking before we dragging, we should handle focus on click in UserInput
            // if (evData.delta.sqrMagnitude > Consts.DragMinDist * Consts.DragMinDist) {
            //     UIFocusablePanel.TryFocus(parent.gameObject);
            // }
        }

        public static void HandleDragPadded(RectTransform rekt, Vector3 d,
                RectOffset padding) {
            rekt.GetWorldCorners(corners);
            int xmul = 1;
            int ymul = 1;
            var dx = new Vector2(d.x, 0);
            var dy = new Vector2(0, d.y);
            var uiScale = Video.GetCurrentUIScale(out _);

            for (int i = 0; i < corners.Length; i++) {
                Vector3 c = corners[i];
                var sc = (Vector2) c;
                var isOver = IsPositionOverGameView(sc, padding, uiScale);
                if (isOver) {
                    if (xmul == 1 && !IsPositionOverGameView(sc + dx, padding, uiScale)) {
                        xmul = 0;
                    }
                    if (ymul == 1 && !IsPositionOverGameView(sc + dy, padding, uiScale)) {
                        ymul = 0;
                    }
                }
            }
            if (xmul != 0 || ymul != 0) {
                rekt.localPosition += new Vector3(d.x * xmul, d.y * ymul, 0);
            }
            // We are clicking before we dragging, we should handle focus on click in UserInput
            // if (d.sqrMagnitude > Consts.DragMinDist * Consts.DragMinDist) {
            //     UIFocusablePanel.TryFocus(rekt.gameObject);
            // }
        }
        public static void HandleDrag(RectTransform rekt, Vector3 d) {
            rekt.GetWorldCorners(corners);
            int xmul = 1;
            int ymul = 1;
            var dx = new Vector2(d.x, 0);
            var dy = new Vector2(0, d.y);
            var yPad = TopBarHeight * Video.GetCurrentUIScale(out _);
            for (int i = 0; i < corners.Length; i++) {
                Vector3 c = corners[i];
                var sc = (Vector2) c;
                // D.Warn("C{0}: {1}", i, sc);
                var isOver = IsPositionOverGameView(sc, yPad: yPad);
                if (isOver) {
                    if (xmul == 1 && !IsPositionOverGameView(sc + dx, yPad: yPad)) {
                        xmul = 0;
                    }
                    if (ymul == 1 && !IsPositionOverGameView(sc + dy, yPad: yPad)) {
                        ymul = 0;
                    }
                }
            }
            if (xmul != 0 || ymul != 0) {
                rekt.localPosition += new Vector3(d.x * xmul, d.y * ymul, 0);
            }
            // We are clicking before we dragging, we should handle focus on click in UserInput
            // if (d.sqrMagnitude > Consts.DragMinDist * Consts.DragMinDist) {
            //     UIFocusablePanel.TryFocus(rekt.gameObject);
            // }
        }
    }
}
