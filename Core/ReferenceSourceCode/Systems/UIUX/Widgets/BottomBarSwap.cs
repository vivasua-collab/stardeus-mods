using System;
using UnityEngine;

namespace Game.Systems.UIUX {
    // Players did not notice the bottom bar changing on selection. The bar slides
    // out of view, gets its new contents while nobody can see it, and slides back,
    // so the change reads as a new bar.
    public sealed class BottomBarSwap {
        private const float OutDuration = 0.08f;
        private const float InDuration = 0.17f;
        private enum Phase { Idle, Out, In }
        private readonly RectTransform rect;
        private readonly Action onHidden;
        private readonly float restY;
        private Phase phase;
        private float t;
        // 0 is at rest, 1 is fully below the screen edge
        private float offset;

        public bool IsPlaying => phase != Phase.Idle;
        public bool IsHiding => phase == Phase.Out;

        public BottomBarSwap(RectTransform rect, Action onHidden) {
            this.rect = rect;
            this.onHidden = onHidden;
            restY = rect.anchoredPosition.y;
        }

        public void Begin() {
            if (phase == Phase.Out) { return; }
            if (phase == Phase.In) {
                // Turn around where the bar is now, the way out eases in with t squared
                t = Mathf.Sqrt(Mathf.Clamp01(offset));
            } else {
                t = 0f;
            }
            phase = Phase.Out;
        }

        public void Tick(float dt) {
            if (phase == Phase.Idle) { return; }
            if (phase == Phase.Out) {
                t += dt / OutDuration;
                if (t >= 1f) {
                    // Switch before swapping, so a request made by the swap itself
                    // turns the bar around instead of being lost
                    phase = Phase.In;
                    t = 0f;
                    offset = 1f;
                    onHidden();
                } else {
                    offset = t * t;
                }
            } else {
                t += dt / InDuration;
                if (t >= 1f) {
                    phase = Phase.Idle;
                    offset = 0f;
                } else {
                    var left = 1f - t;
                    offset = left * left * left;
                }
            }
            Place();
        }

        public void Stop() {
            phase = Phase.Idle;
            t = 0f;
            offset = 0f;
            Place();
        }

        private void Place() {
            var height = rect.rect.height * rect.localScale.y;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, restY - offset * height);
        }
    }
}
