using Game.Constants;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.UI {
    public sealed class NewEventIcon : MonoBehaviour {
        [SerializeField] private RectTransform rekt;
        [SerializeField] private CanvasGroup cg;
        [SerializeField] private bool fade = true;
        private Vector2 initialPosition;
        private Vector3 initialScale;
        private float animTimer;
        private int targetIterations;
        private int currentIteration;
        private bool isPlaying;
        private bool isReady;
        public bool IsPlaying => isPlaying;
        private float idleDuration;
        private float totalCycleDuration;
        private float accumulatedScale;
        private const float FadeInDuration = 0.3f;
        private const float ShakeDuration = 0.3f;
        private const float PunchDuration = 0.3f;
        private const float FadeOutDuration = 0.3f;

        public void Play(int times, float delay = 2f) {
            if (!isReady) {
                D.Log("Trying to play too early! {0}", D.GOPath(transform));
                return;
            }
            if (isPlaying) {
                D.ErrSoft("Playing without stopped first! {0}", D.GOPath(transform));
            }
            accumulatedScale = 1f;
            targetIterations = times;
            currentIteration = 0;
            animTimer = 0f;
            isPlaying = true;
            idleDuration = delay;
            totalCycleDuration = FadeInDuration + ShakeDuration + PunchDuration + FadeOutDuration + idleDuration;
            rekt.anchoredPosition = initialPosition;
            rekt.localScale = initialScale;
            if (fade) {
                cg.alpha = 0f;
            }
        }

        public void Stop() {
            isPlaying = false;
            accumulatedScale = 1f;
            if (fade) {
                cg.alpha = 0f;
            }
        }

        private void Awake() {
            initialPosition = rekt.anchoredPosition;
            initialScale = rekt.localScale;
            if (fade) {
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
            isReady = true;
        }

        private void Update() {
            if (!isPlaying) {
                return;
            }

            animTimer += Time.unscaledDeltaTime;
            if (animTimer >= totalCycleDuration) {
                animTimer -= totalCycleDuration;
                currentIteration++;

                // Check if we should stop (only if targetIterations > 0)
                // currentIteration now represents completed iterations
                if (targetIterations > 0 && currentIteration >= targetIterations) {
                    isPlaying = false;
                    if (fade) {
                        cg.alpha = 0f;
                    }
                    return;
                }
            }

            if (animTimer < FadeInDuration) {
                // Fade in phase
                if (fade) {
                    float fadeProgress = animTimer / FadeInDuration;
                    cg.alpha = fadeProgress;
                }
                rekt.anchoredPosition = initialPosition;
                rekt.localScale = initialScale * accumulatedScale;
            } else if (animTimer < FadeInDuration + ShakeDuration) {
                // Shake phase
                if (fade) {
                    cg.alpha = 1f;
                }
                float shakeTime = animTimer - FadeInDuration;
                float shakeProgress = shakeTime / ShakeDuration;
                float shakeAmount = Mathf.Sin(shakeProgress * Mathf.PI * 10f) * 5f * (1f - shakeProgress);
                rekt.anchoredPosition = initialPosition + new Vector2(shakeAmount, 0f);
                if (MiscUtils.IsApril1st) {
                    accumulatedScale += 1f * Time.unscaledDeltaTime;
                }
                rekt.localScale = initialScale * accumulatedScale;
            } else if (animTimer < FadeInDuration + ShakeDuration + PunchDuration) {
                // Punch scale phase
                if (fade) {
                    cg.alpha = 1f;
                }
                float punchTime = animTimer - FadeInDuration - ShakeDuration;
                float punchProgress = punchTime / PunchDuration;
                float punchScale = 1f + Mathf.Sin(punchProgress * Mathf.PI) * 0.3f;

                // When scaling with a center pivot, keep the anchored position the same
                // The visual scaling will happen around the pivot point naturally
                rekt.localScale = initialScale * accumulatedScale * punchScale;
                rekt.anchoredPosition = initialPosition;
            } else if (animTimer < FadeInDuration + ShakeDuration + PunchDuration + FadeOutDuration) {
                // Fade out phase
                float fadeOutTime = animTimer - FadeInDuration - ShakeDuration - PunchDuration;
                if (fade) {
                    float fadeProgress = fadeOutTime / FadeOutDuration;
                    cg.alpha = 1f - fadeProgress;
                }
                rekt.anchoredPosition = initialPosition;
                rekt.localScale = initialScale * accumulatedScale;
            } else {
                // Idle phase - invisible
                if (fade) {
                    cg.alpha = 0f;
                }
                rekt.anchoredPosition = initialPosition;
                rekt.localScale = initialScale * accumulatedScale;
            }
        }
    }
}
