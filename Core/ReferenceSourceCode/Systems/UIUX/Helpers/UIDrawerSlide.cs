using System;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    // Slides a panel out from under the edge above it, like a drawer, and back in. The clip is
    // an ancestor of the panel whose top edge is that edge: while it slides, the panel is cut
    // there so it never draws over what it comes out from under. At rest the clip is off, so a
    // panel that is dragged, resized or overflows is never cut. The clip's rect needs some area:
    // a RectMask2D on a zero-height rect hides everything, whatever its padding.
    public sealed class UIDrawerSlide : MonoBehaviour {
        // Only the top edge cuts; the other sides reach this far past the clip's rect
        private const float Unbounded = 10000f;
        // Opening spawns the panel's rows, and that frame can take a tenth of a second. A
        // longer frame advances the slide only this much, so it is not over in two frames.
        private const float MaxStepSeconds = 1f / 30f;

        [SerializeField] private RectMask2D clip;
        [SerializeField] private RectTransform panel;
        [SerializeField] private float seconds = 0.2f;
        // How far the panel's frame is drawn past its rect on each side. The clip lets it show
        // above the edge, so the last frame of the slide matches the panel at rest, and the
        // panel travels that much further to be out of sight.
        [SerializeField] private float frameOverhang;

        private Func<float> distance;
        private Action onHidden;
        private Vector2 restPos;
        private bool isInitialized;
        private bool isSliding;
        // 0 hidden, 1 shown
        private float progress = 1f;
        private bool opening = true;

        private void Awake() {
            Init();
            enabled = isSliding;
        }

        // How far the panel travels, read every frame: its content can change size mid-slide
        public void SetDistance(Func<float> distance) {
            this.distance = distance;
        }

        public void Show() {
            Init();
            onHidden = null;
            opening = true;
            if (progress >= 1f) { return; }
            StartSliding();
        }

        public void Hide(Action onHidden) {
            Init();
            this.onHidden = onHidden;
            opening = false;
            if (progress <= 0f) {
                Finish();
                return;
            }
            StartSliding();
        }

        // Jumps to an end without the slide and drops a pending hide callback
        public void Snap(bool shown) {
            Init();
            onHidden = null;
            opening = shown;
            progress = shown ? 1f : 0f;
            AtRest();
        }

        private void LateUpdate() {
            var step = Mathf.Min(GameTimer.unscaledDeltaTime, MaxStepSeconds)
                / Mathf.Max(seconds, 0.01f);
            progress = Mathf.Clamp01(progress + (opening ? step : -step));
            if (opening ? progress >= 1f : progress <= 0f) {
                Finish();
                return;
            }
            Apply();
        }

        private void StartSliding() {
            isSliding = true;
            clip.enabled = true;
            enabled = true;
            Apply();
        }

        // Ease out when opening. Closing runs the same curve backwards, so it eases in, and
        // turning around mid-slide carries on from where the panel is.
        private void Apply() {
            var t = 1f - progress;
            var hidden = t * t * t;
            var d = (distance != null ? distance() : panel.rect.height) + frameOverhang * 2f;
            panel.anchoredPosition = new Vector2(restPos.x, restPos.y + hidden * d);
        }

        private void Finish() {
            if (!opening) {
                var callback = onHidden;
                onHidden = null;
                callback?.Invoke();
            }
            AtRest();
        }

        private void AtRest() {
            isSliding = false;
            panel.anchoredPosition = restPos;
            clip.enabled = false;
            enabled = false;
        }

        // Before the first slide, while the panel is still where it rests
        private void Init() {
            if (isInitialized) { return; }
            isInitialized = true;
            restPos = panel.anchoredPosition;
            clip.padding = new Vector4(-Unbounded, -Unbounded, -Unbounded, -frameOverhang);
            clip.enabled = false;
        }
    }
}
