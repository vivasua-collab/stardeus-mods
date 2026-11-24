using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // A screen-sized outline that shrinks onto one widget, then pulses and fades. Pulls the
    // eye from wherever it was to a small HUD element: playtesters kept missing new quests
    // even though the quest widget's "+1" badge pulses, because nothing on screen pointed at
    // it. Never blocks input. A request made while a dialog, the Codex or a tutorial is up
    // waits for them to close, so the effect always plays where the player can see it.
    // Rings on the quest tracker also wait for the Quests window: it lists the same quests,
    // and rings on the tracker while clicking through it were distracting. Other rings do
    // not, or an alert ring would stay silent for as long as a player keeps that window open.
    // The target must be visible too, which includes its CanvasGroups: the Research screen
    // hides the HUD by fading it out, and a ring there outlined an empty spot on the tree.
    // Each target gets its own ring. With one shared ring, a request for another widget
    // retargeted a ring still waiting behind a dialog, and the first widget never rang.
    // A ring can also start on a world entity's selection outline instead of the screen edges
    // (RequestFrom): the selection's actions in the bottom bar are new to a player who just
    // clicked something and is looking at it, and a ring from the edges says "look here" but
    // not "this belongs to what you clicked". It follows the entity while it travels.
    public sealed class UIFocusRing : MonoBehaviour {
        private const float ShrinkTime = 1.0f;
        private const float PulseTime = 1.25f;
        private const float FadeTime = 0.5f;
        private const float Thickness = 2f;
        // How far the ring sits outside the target, so it frames the widget, not its text
        private const float Padding = 3f;
        private const int SortOrder = 20;

        private static readonly List<UIFocusRing> rings = new();

        private UIRingOverlay overlay;
        private RectTransform target;
        private Color color;
        private int priority;
        private bool waitsForQuestsWindow;
        // Plays over the main menu instead of waiting for it to close
        private bool isInMenu;
        private bool isPending;
        private float elapsed;
        private bool isPlaying;
        // Where the ring starts, when not the screen edges
        private Entity source;
        private Rect sourceRect;
        private bool hasSourceRect;
        private bool isSourceOffScreen;

        public static bool DebugIsPendingOn(RectTransform target) {
            var ring = Find(target);
            return ring != null && ring.isPending;
        }
        public static bool DebugIsPlayingOn(RectTransform target) {
            var ring = Find(target);
            return ring != null && ring.isPlaying;
        }
        public static Color DebugColorOn(RectTransform target) {
            var ring = Find(target);
            return ring != null ? ring.color : Color.clear;
        }
        public static Entity DebugSourceOn(RectTransform target) {
            var ring = Find(target);
            return ring != null && (ring.isPending || ring.isPlaying) ? ring.source : null;
        }

        public static bool IsActiveOn(RectTransform target) {
            var ring = Find(target);
            return ring != null && (ring.isPending || ring.isPlaying);
        }

        public static void Request(RectTransform target, bool waitForQuestsWindow = false) {
            Request(target, Texts.TextColorAccented, 0, waitForQuestsWindow);
        }

        // Plays the ring on `target` as soon as nothing covers the screen. A request while
        // its ring waits or plays does not restart it, since alerts and offers come in
        // bursts; a higher `priority` only takes over its color.
        public static void Request(RectTransform target, Color color, int priority,
                bool waitForQuestsWindow = false) {
            if (target == null) { return; }
            if (UIRingOverlay.IsSuppressed) { return; }
            var ring = Find(target);
            if (ring == null && !TryCreate(target, out ring)) { return; }
            ring.Enqueue(color, priority, waitForQuestsWindow);
        }

        // Like Request, but the ring starts on `source`'s selection outline and travels onto
        // `target`. A ring already waiting or playing there starts over, since it now comes
        // from somewhere else.
        public static void RequestFrom(RectTransform target, Entity source, Color color) {
            if (target == null || source == null) { return; }
            if (UIRingOverlay.IsSuppressed) { return; }
            var ring = Find(target);
            if (ring == null && !TryCreate(target, out ring)) { return; }
            ring.Stop();
            ring.source = source;
            ring.Enqueue(color, 0, false);
        }

        // For a widget of the main menu itself (the new game setup). Other rings wait while
        // the menu covers the HUD, and they draw below the menu's canvas.
        public static void RequestInMenu(RectTransform target) {
            if (target == null) { return; }
            if (UIRingOverlay.IsSuppressed) { return; }
            var ring = Find(target);
            if (ring == null) {
                var canvas = target.GetComponentInParent<Canvas>();
                var order = canvas != null
                    ? Mathf.Max(SortOrder, canvas.sortingOrder + 1) : SortOrder;
                if (!TryCreate(target, order, out ring)) { return; }
            }
            ring.isInMenu = true;
            ring.Enqueue(Texts.TextColorAccented, 0, false);
        }

        // Stops a ring waiting or playing on `target`, for one that no longer points at
        // anything true
        public static void Cancel(RectTransform target) {
            var ring = Find(target);
            if (ring != null && (ring.isPending || ring.isPlaying)) {
                ring.Stop();
            }
        }

        private static UIFocusRing Find(RectTransform target) {
            if (target == null) { return null; }
            for (int i = rings.Count - 1; i >= 0; i--) {
                var ring = rings[i];
                if (ring == null) {
                    // Destroyed along with its canvas
                    rings.RemoveAt(i);
                    continue;
                }
                if (ring.target == null) {
                    // Its widget is gone and an idle ring never updates to notice
                    rings.RemoveAt(i);
                    Destroy(ring.gameObject);
                    continue;
                }
                if (ring.target == target) { return ring; }
            }
            return null;
        }

        private static bool TryCreate(RectTransform target, out UIFocusRing ring) {
            return TryCreate(target, SortOrder, out ring);
        }

        private static bool TryCreate(RectTransform target, int sortOrder,
                out UIFocusRing ring) {
            ring = null;
            var overlay = UIRingOverlay.Create("FocusRing", target, sortOrder);
            if (overlay == null) { return false; }
            ring = overlay.GO.AddComponent<UIFocusRing>();
            ring.overlay = overlay;
            ring.target = target;
            rings.Add(ring);
            return true;
        }

        private void Enqueue(Color color, int priority, bool waitForQuestsWindow) {
            bool isActive = isPending || isPlaying;
            // A request merged into a waiting or playing ring keeps its hold
            waitsForQuestsWindow = waitForQuestsWindow || (isActive && waitsForQuestsWindow);
            if (isActive && priority <= this.priority) { return; }
            this.color = color;
            this.priority = priority;
            overlay.SetColor(color);
            if (isActive) { return; }
            isPending = true;
            enabled = true;
        }

        // Whether a ring requested on `target` now would start playing at once rather than wait.
        // Lets a caller skip a ring that only makes sense right now: the pause button ring,
        // waiting behind a dialog or the hidden UI, would play after the player resumed and point
        // at a pause that is over.
        public static bool CanShowNow(RectTransform target) {
            if (target == null || UIRingOverlay.IsSuppressed) { return false; }
            return !IsScreenCovered(false, false) && UIRingOverlay.IsTargetVisible(target);
        }

        private static bool IsScreenCovered(bool waitsForQuestsWindow, bool isInMenu) {
            return (UIShowing.MainMenu && !isInMenu) || UIShowing.Codex || UIShowing.Tutorial
                || (waitsForQuestsWindow && UIShowing.Quests)
                || (The.Dialog != null && The.Dialog.IsBusy);
        }

        private void Update() {
            if (target == null) {
                // The widget is gone for good, and so is any use for this ring
                Destroy(gameObject);
                return;
            }
            bool canShow = !IsScreenCovered(waitsForQuestsWindow, isInMenu)
                && UIRingOverlay.IsTargetVisible(target);
            if (isPlaying && !canShow) {
                // Hidden mid-ring (Research opened): play it again in full once it is back
                isPlaying = false;
                isPending = true;
                overlay.SetAlpha(0f);
            }
            if (isPending) {
                if (!canShow) { return; }
                isPending = false;
                isPlaying = true;
                elapsed = 0f;
                hasSourceRect = false;
                overlay.BringToFront();
            }
            if (!isPlaying) {
                enabled = false;
                return;
            }
            elapsed += GameTimer.unscaledDeltaTime;
            var total = ShrinkTime + PulseTime + FadeTime;
            if (elapsed >= total) {
                Stop();
                return;
            }
            Layout();
        }

        private void Layout() {
            var to = overlay.TargetRect(target, Padding);
            float alpha;
            float thickness = Thickness;
            Rect r;
            if (elapsed < ShrinkTime && source != null) {
                var t = elapsed / ShrinkTime;
                // Ease in-out cubic: leaves the entity slowly enough for the eye to follow
                t = t < 0.5f ? 4f * t * t * t : 1f - 4f * (1f - t) * (1f - t) * (1f - t);
                r = Lerp(SourceRect(), to, t);
                alpha = 1f;
            } else if (elapsed < ShrinkTime) {
                var t = elapsed / ShrinkTime;
                // Ease out cubic: fast from the edges, settling onto the widget
                t = 1f - (1f - t) * (1f - t) * (1f - t);
                r = Lerp(overlay.FullRect, to, t);
                alpha = Mathf.Lerp(0.35f, 1f, t);
            } else if (elapsed < ShrinkTime + PulseTime) {
                var t = (elapsed - ShrinkTime) / PulseTime;
                r = to;
                alpha = 1f;
                // Two beats
                thickness = Thickness * (1f + 0.6f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)));
            } else {
                var t = (elapsed - ShrinkTime - PulseTime) / FadeTime;
                r = to;
                alpha = 1f - t;
            }
            overlay.SetAlpha(alpha);
            overlay.Draw(r, thickness);
        }

        // The source's selection outline on screen, the rect SelectionVFX draws. It follows a
        // walking being or a panning camera, and stays where it was once the entity is gone.
        // An entity off screen when the ring starts leaves it to start from the screen edges.
        private Rect SourceRect() {
            var cam = RenderUtils.Camera;
            if (source.IsActive && cam != null) {
                var center = source.SelectPosition;
                var half = source.Size * 0.5f;
                Vector2 a = cam.WorldToScreenPoint(
                    new Vector3(center.x - half.x, center.y - half.y));
                Vector2 b = cam.WorldToScreenPoint(
                    new Vector3(center.x + half.x, center.y + half.y));
                var r = overlay.ScreenRect(Vector2.Min(a, b), Vector2.Max(a, b), Padding);
                if (!hasSourceRect) {
                    hasSourceRect = true;
                    isSourceOffScreen = !r.Overlaps(overlay.FullRect);
                }
                sourceRect = r;
            }
            return !hasSourceRect || isSourceOffScreen ? overlay.FullRect : sourceRect;
        }

        private static Rect Lerp(Rect a, Rect b, float t) {
            return Rect.MinMaxRect(
                Mathf.Lerp(a.xMin, b.xMin, t), Mathf.Lerp(a.yMin, b.yMin, t),
                Mathf.Lerp(a.xMax, b.xMax, t), Mathf.Lerp(a.yMax, b.yMax, t));
        }

        private void Stop() {
            isPlaying = false;
            isPending = false;
            source = null;
            overlay?.SetAlpha(0f);
            enabled = false;
        }

        private void OnDestroy() {
            rings.Remove(this);
        }
    }
}
