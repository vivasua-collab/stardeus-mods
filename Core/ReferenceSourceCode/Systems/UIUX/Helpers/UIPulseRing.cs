using System.Collections.Generic;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // The reverse of UIFocusRing: an outline that leaves a widget, spreads outward and fades
    // quickly. Says "something just happened here" without dragging the eye across the
    // screen. The notification hub uses it for new alerts, colored by their urgency, and the
    // top bar electricity widget for a deficit that keeps coming back.
    // Alerts come in bursts (a raid, a save load), so a request while a ring plays or cools
    // down is dropped unless it is more urgent: one ring per burst, in its most urgent color.
    // Waits for covering screens and a hidden target the same way UIFocusRing does.
    // A request can spread wider: the hub makes urgent alerts reach further, so the size
    // tells urgency apart even when the color alone does not.
    // Each target gets its own ring and cooldown, like UIFocusRing. A deficit pulses both the
    // hub (its alert) and the electricity widget in the same tick, and with one shared ring
    // the second request was dropped as part of the first one's burst.
    public sealed class UIPulseRing : MonoBehaviour {
        private const float SpreadTime = 0.6f;
        private const float Spread = 40f;
        private const float StartThickness = 3f;
        private const float EndThickness = 1f;
        private const float Padding = 3f;
        // Measured from the start of the last ring
        private const float Cooldown = 3f;
        // Below UIFocusRing, which points somewhere and wins if both play
        private const int SortOrder = 19;

        private static readonly List<UIPulseRing> rings = new();

        private UIRingOverlay overlay;
        private RectTransform target;
        private Color color;
        private int priority;
        private float spreadScale = 1f;
        private float spreadTime = SpreadTime;
        private bool isPending;
        private bool isPlaying;
        private float elapsed;
        private double lastStart = double.MinValue;

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

        public static bool IsActiveOn(RectTransform target) {
            var ring = Find(target);
            return ring != null && (ring.isPending || ring.isPlaying);
        }

        // A higher `priority` wins over a pending or recent ring, and brings its color and
        // `spreadScale` (a multiplier of how far the ring travels) along
        public static void Request(RectTransform target, Color color, int priority,
                float spreadScale = 1f) {
            if (target == null) { return; }
            if (UIRingOverlay.IsSuppressed) { return; }
            var ring = Find(target);
            if (ring == null && !TryCreate(target, out ring)) { return; }
            ring.Enqueue(color, priority, spreadScale);
        }

        private static UIPulseRing Find(RectTransform target) {
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

        private static bool TryCreate(RectTransform target, out UIPulseRing ring) {
            ring = null;
            var overlay = UIRingOverlay.Create("PulseRing", target, SortOrder);
            if (overlay == null) { return false; }
            ring = overlay.GO.AddComponent<UIPulseRing>();
            ring.overlay = overlay;
            ring.target = target;
            rings.Add(ring);
            return true;
        }

        private void Enqueue(Color color, int priority, float spreadScale) {
            bool isRecent = isPlaying
                || GameTimer.realtimeSinceStartupAsDouble - lastStart < Cooldown;
            if ((isPending || isRecent) && priority <= this.priority) { return; }
            this.color = color;
            this.priority = priority;
            this.spreadScale = spreadScale;
            // A wider ring also travels faster, but only by the square root (4x spreads twice
            // as fast), so it lasts longer: at the same duration a big ring flashed past
            spreadTime = SpreadTime * Mathf.Sqrt(spreadScale);
            isPending = true;
            enabled = true;
        }

        private static bool IsScreenCovered() {
            return UIShowing.MainMenu || UIShowing.Codex || UIShowing.Tutorial
                || (The.Dialog != null && The.Dialog.IsBusy);
        }

        private void Update() {
            if (target == null) {
                // The widget is gone for good, and so is any use for this ring
                Destroy(gameObject);
                return;
            }
            bool canShow = !IsScreenCovered() && UIRingOverlay.IsTargetVisible(target);
            if (isPlaying && !canShow) {
                // Hidden mid-ring: play it again in full once the target is back
                isPlaying = false;
                isPending = true;
                overlay.SetAlpha(0f);
            }
            if (isPending) {
                if (!canShow) { return; }
                isPending = false;
                isPlaying = true;
                elapsed = 0f;
                lastStart = GameTimer.realtimeSinceStartupAsDouble;
                overlay.SetColor(color);
                overlay.BringToFront();
            }
            if (!isPlaying) {
                enabled = false;
                return;
            }
            elapsed += GameTimer.unscaledDeltaTime;
            if (elapsed >= spreadTime) {
                Stop();
                return;
            }
            Layout();
        }

        private void Layout() {
            var t = elapsed / spreadTime;
            // Ease out cubic: leaves the widget fast, slows down as it fades
            var eased = 1f - (1f - t) * (1f - t) * (1f - t);
            var r = overlay.TargetRect(target, Padding);
            var grow = Spread * spreadScale * eased;
            r = Rect.MinMaxRect(r.xMin - grow, r.yMin - grow, r.xMax + grow, r.yMax + grow);
            overlay.SetAlpha(1f - t);
            overlay.Draw(r, Mathf.Lerp(StartThickness, EndThickness, eased));
        }

        private void Stop() {
            isPlaying = false;
            isPending = false;
            overlay?.SetAlpha(0f);
            enabled = false;
        }

        private void OnDestroy() {
            rings.Remove(this);
        }
    }
}
