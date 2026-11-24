#if !PRODUCTION_BUILD
// #define DEBUG_SUBPANEL
#endif
using System;
using System.Diagnostics.CodeAnalysis;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class SubPanelWidget : BaseWidget {
        [SerializeField] private RectTransform parentConnector;
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect mainScroll;
        [SerializeField] private Scrollbar mainScrollBar;
        [SerializeField] private GameObject resizeHandle;
        public WGroup Container;
        public WGroup Footer;
        public bool IsShowing;
        private WBase showingFor;
        public Lifetime Lifetime;
        [ClearOnReload]
        public static string IsOpenFor;
        public bool IsShowingFor(WBase w) {
            return IsShowing && showingFor == w;
        }

        public void Initialize() {
            wPanelHeader.SetOnClick(Close);
            this.SetActiveFast(false);
            ShowFooter(false);
            Footer?.DestroyChildren();
        }
        public void ShowFooter(bool yes) {
            Footer?.SetActiveFast(yes);
        }


        public void Show([NotNull] WBase forItem, bool toggle = true) {
            if (!toggle && forItem != null) {
                showingFor = null;
            }
            Show(forItem, null);
        }

        public void Show([NotNull] WBase forItem, string forId) {
            IsOpenFor = forId;
            #if DEBUG_SUBPANEL
            D.Warn("Showing subpanel for {0}", forId);
            #endif
            if (IsShowing) {
                if (showingFor == forItem) {
                    Close();
                }
                showingFor = forItem;
                RebuildLayout();
                RepositionWidget(forItem, false);
                return;
            }
            UIShowing.Set(UIShowingFlags.SubPanel);
            showingFor = forItem;
            LockMainScroll();
            IsShowing = true;
            this.SetActiveFast(true);
            ShowParentConnector(forItem);
            resizeHandle.SetActiveFast(false);
            RebuildLayout();
            RepositionWidget(forItem, false);
        }

        // The parent list must not scroll while the sub panel hangs off one of its
        // rows. Block input rather than disabling the ScrollRect: a disabled
        // ScrollRect drops out of the layout pass and freezes its scrollbar inset,
        // which on a cold open was decided against the prefab height. Wheel and
        // drag honour the axis flags; scrollbar drags don't, hence interactable.
        private void LockMainScroll() {
            mainScroll.vertical = false;
            mainScrollBar.interactable = false;
        }

        private void UnlockMainScroll() {
            mainScroll.vertical = true;
            mainScrollBar.interactable = true;
        }

        public void ShowParentConnector(WBase forItem) {
            var sd = parentConnector.sizeDelta;
            parentConnector.sizeDelta = new Vector2(sd.x, forItem.Rect.rect.height);
            parentConnector.SetActiveFast(true);
        }

        public void RepositionWidget(WBase forItem, bool connectorOnly) {
            // Get forItem's vertical center in screen space
            forItem.Rect.GetWorldCorners(Caches.Corners);
            // var itemBottomY = RectTransformUtility.WorldToScreenPoint(cam, Caches.Corners[0]).y;
            var itemTopY = Caches.Corners[1].y;
            var itemBottomY = Caches.Corners[0].y;

            // Get shared parent's top Y in screen space
            var sharedParent = parentConnector.parent as RectTransform;
            sharedParent.GetWorldCorners(Caches.Corners);
            var parentTopY = Caches.Corners[1].y;

            var uiScale = Video.GetCurrentUIScale(out _);

            // Position connector vertically aligned with forItem center
            var connectorOffsetY = itemTopY - parentTopY;
            connectorOffsetY /= uiScale;
            parentConnector.anchoredPosition = new Vector2(
                parentConnector.anchoredPosition.x, connectorOffsetY);

            if (connectorOnly) { return; }

            // Convert to anchored position (offset from parent top)
            var widgetTopOffset = itemTopY - parentTopY;
            widgetTopOffset /= uiScale;
            Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, widgetTopOffset);

            // If widget goes off screen bottom, push it up. Never above the row it hangs off,
            // though: for a row near the bottom of the screen that left the connector
            // dangling below the panel.
            Rect.GetWorldCorners(Caches.Corners);
            var widgetBottomScreenY = Caches.Corners[0].y;
            const float padding = 32;
            var bottomLimit = Mathf.Min(padding, Mathf.Max(0f, itemBottomY));
            if (widgetBottomScreenY < bottomLimit) {
                // Screen pixels to anchored units, as in the top check below
                Rect.anchoredPosition = new Vector2(
                    Rect.anchoredPosition.x,
                    Rect.anchoredPosition.y + (bottomLimit - widgetBottomScreenY) / uiScale);
            }

            // If widget top is off screen top, push it down
            Rect.GetWorldCorners(Caches.Corners);
            var widgetTopScreenY = Caches.Corners[1].y;
            var screenHeight = Screen.height;
            const float paddingTop = 48;
            if (widgetTopScreenY > screenHeight - paddingTop) {
                Rect.anchoredPosition = new Vector2(
                    Rect.anchoredPosition.x,
                    Rect.anchoredPosition.y - (widgetTopScreenY - (screenHeight - paddingTop)) / uiScale);
            }

            // If widget right is off screen right, push the parent widget left
            Rect.GetWorldCorners(Caches.Corners);
            var widgetRightScreenX = Caches.Corners[2].x;
            var screenWidth = Screen.width;
            const float paddingRight = 24;
            if (widgetRightScreenX > screenWidth - paddingRight) {
                var parent = parentConnector.parent as RectTransform;
                parent.anchoredPosition = new Vector2(
                    parent.anchoredPosition.x - (widgetRightScreenX - (screenWidth - paddingRight)) / uiScale,
                    parent.anchoredPosition.y);
            }
            Rect.parent.SetAsLastSibling();
        }

        public void Close() {
            Close(true);
        }

        public void Close(bool clearMemory) {
            #if DEBUG_SUBPANEL
            D.Warn("Closing subpanel for {0} (clearing: {1})", IsOpenFor, clearMemory);
            #endif

            DisposeContents();
            showingFor = null;
            if (clearMemory) {
                #if DEBUG_SUBPANEL
                D.Warn("Closing subpanel with clear mem: {0}", IsOpenFor);
                #endif
                IsOpenFor = null;
            }
            if (IsShowing) {
                UnlockMainScroll();
                resizeHandle.SetActiveFast(true);
            }
            IsShowing = false;
            this.SetActiveFast(false);
            ShowFooter(false);
            parentConnector.SetActiveFast(false);
            UIShowing.Unset(UIShowingFlags.SubPanel);
        }

        public void DisposeContents() {
            if (Lifetime != null) {
                Lifetime.Dispose();
                Lifetime = null;
            }
        }

        public void SetTitle(string title) {
            wPanelHeader.SetText(title);
        }

        public Lifetime CreateLifetime(Lifetime parent) {
            Lifetime?.Dispose();
            Lifetime = parent.CreateChild("SubPanelWidget");
            return Lifetime;
        }
    }
}
