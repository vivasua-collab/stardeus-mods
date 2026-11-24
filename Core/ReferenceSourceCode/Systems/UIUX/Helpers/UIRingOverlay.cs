using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    // The drawing half of the HUD rings (UIFocusRing, UIPulseRing): a click-through overlay
    // stretched over the root canvas, with four bars that outline a rect in its space. The
    // rings only decide where that rect is and how it animates.
    public sealed class UIRingOverlay {
        // Rings stay off while tests run, so quest and notification tests don't spawn
        // overlays; ring tests switch them back on
        public static bool DebugAllowInTests;
        // Set while the scripted capture tooling (Assets/Scripts/Capture) records: a ring
        // sweeping across the HUD pulls the eye off what a clip shows. Not dev-only: captures
        // run in non-development builds, which have no watermark.
        public static bool SuppressedForCapture;

        public readonly GameObject GO;
        private readonly RectTransform root;
        private readonly RectTransform canvasRect;
        // Null for an overlay canvas, whose space is screen pixels already
        private readonly Camera canvasCamera;
        private readonly CanvasGroup group;
        private readonly Image[] bars = new Image[4];
        private readonly Vector3[] corners = new Vector3[4];

        public static bool IsSuppressed {
            get {
                if (SuppressedForCapture) { return true; }
                #if !PRODUCTION_BUILD
                if (The.TestsAreRunning && !DebugAllowInTests) { return true; }
                #endif
                return false;
            }
        }

        // HUD widgets are hidden by fading their CanvasGroup (WidgetRegistry.ToggleAll while
        // Research is open), not by deactivating them, so being active is not enough: a ring
        // on such a widget would outline an empty spot on top of the full-screen view.
        public static bool IsTargetVisible(RectTransform target) {
            if (!target.gameObject.activeInHierarchy) { return false; }
            Transform t = target;
            while (t != null) {
                if (t.TryGetComponent<CanvasGroup>(out var cg) && cg.enabled) {
                    if (cg.alpha <= 0.01f) { return false; }
                    if (cg.ignoreParentGroups) { return true; }
                }
                if (t.TryGetComponent<Canvas>(out var canvas) && canvas.isRootCanvas) {
                    return true;
                }
                t = t.parent;
            }
            return true;
        }

        public static UIRingOverlay Create(string name, RectTransform target, int sortOrder) {
            var rootCanvas = PopoutBlocker.GetRootCanvasFor(target.gameObject);
            if (rootCanvas == null) {
                D.Err("UIRingOverlay: no root canvas for {0}", target);
                return null;
            }
            return new UIRingOverlay(name, rootCanvas, sortOrder);
        }

        private UIRingOverlay(string name, Canvas rootCanvas, int sortOrder) {
            GO = new GameObject(name, typeof(RectTransform));
            GO.layer = rootCanvas.gameObject.layer;
            root = (RectTransform) GO.transform;
            root.SetParent(rootCanvas.transform, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.sizeDelta = Vector2.zero;
            var canvas = GO.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = rootCanvas.sortingLayerID;
            canvas.sortingOrder = sortOrder;
            group = GO.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            group.ignoreParentGroups = true;
            canvasRect = (RectTransform) rootCanvas.transform;
            canvasCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : rootCanvas.worldCamera;
            for (int i = 0; i < bars.Length; i++) {
                var bar = new GameObject("Bar", typeof(RectTransform));
                bar.layer = GO.layer;
                var brt = (RectTransform) bar.transform;
                brt.SetParent(root, false);
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                var img = bar.AddComponent<Image>();
                img.raycastTarget = false;
                bars[i] = img;
            }
            group.alpha = 0f;
        }

        public void BringToFront() {
            root.SetAsLastSibling();
        }

        public void SetAlpha(float alpha) {
            group.alpha = alpha;
        }

        public void SetColor(Color color) {
            for (int i = 0; i < bars.Length; i++) {
                bars[i].color = color;
            }
        }

        // The whole canvas, in the same centered space as TargetRect
        public Rect FullRect {
            get {
                // The canvas rect is relative to its pivot
                var r = canvasRect.rect;
                r.position -= r.center;
                return r;
            }
        }

        // The target's corners in the local space of the root canvas, which is also the
        // space of this overlay (it stretches over the whole canvas)
        public Rect TargetRect(RectTransform target, float padding) {
            target.GetWorldCorners(corners);
            var min = (Vector2) canvasRect.InverseTransformPoint(corners[0]);
            var max = (Vector2) canvasRect.InverseTransformPoint(corners[2]);
            // Canvas local space has its origin at the canvas pivot; the bars are anchored to
            // the overlay's center, which is the same point for a centered pivot
            var offset = canvasRect.rect.center;
            min -= offset;
            max -= offset;
            return Rect.MinMaxRect(min.x - padding, min.y - padding,
                max.x + padding, max.y + padding);
        }

        // A rect given in screen pixels (a world position through the game camera), in the
        // same centered space as TargetRect
        public Rect ScreenRect(Vector2 screenMin, Vector2 screenMax, float padding) {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenMin,
                canvasCamera, out var min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenMax,
                canvasCamera, out var max);
            var offset = canvasRect.rect.center;
            min -= offset;
            max -= offset;
            return Rect.MinMaxRect(min.x - padding, min.y - padding,
                max.x + padding, max.y + padding);
        }

        public void Draw(Rect r, float thickness) {
            SetBar(0, r.center.x, r.yMax, r.width + thickness, thickness);
            SetBar(1, r.center.x, r.yMin, r.width + thickness, thickness);
            SetBar(2, r.xMin, r.center.y, thickness, r.height + thickness);
            SetBar(3, r.xMax, r.center.y, thickness, r.height + thickness);
        }

        private void SetBar(int idx, float x, float y, float w, float h) {
            var rt = bars[idx].rectTransform;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
