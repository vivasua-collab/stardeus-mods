using System;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Pool;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Uween;

namespace Game.Systems.Ships {
    public sealed class ForceFieldView {
        private readonly GameState S;
        private Transform shieldVisuals;
        private Tween currentTween;
        private Transform prefab;
        private Vector3 wantedPos;
        public Vector2 Center;
        public float Radius { get; private set; }
        public bool IsUp;
        public bool IsVisualUp => IsUp && currentTween == null;
        public bool IsAnimating => currentTween != null;
        public ForceFieldView(GameState s) {
            S = s;
            fx = "ForceFieldFX";
        }
        private string fx;
        public void SetFX(string fx) {
            this.fx = fx;
            prefab = null;
        }
        public void CopyTransform(Transform tr) {
            if (shieldVisuals != null) {
                wantedPos = tr.position;
                shieldVisuals.rotation = tr.rotation;
            }
        }
        public void Enable(Vector3 position, float scale, bool instant = false) {
            // D.Warn("Enabling shield");
            Center = position;
            wantedPos = position;
            IsUp = true;
            Radius = scale * 0.5f;
            if (shieldVisuals != null) {
                shieldVisuals.position = position;
                return;
            }
            prefab ??= ResourceLibrary.FindPrefab<Transform>(fx);
            var sv = PrefabPool.Spawn(prefab, position,
                Consts.QId, ResourceLibrary.Root);
            if (currentTween != null) {
                currentTween.Skip();
                currentTween = null;
            }
            sv.localScale = Consts.V3Zero;
            shieldVisuals = sv;
            var shieldScale = scale;
            if (instant) {
                sv.localScale = Consts.V3One * shieldScale;
                return;
            }
            Sounds.PlayLoud(Center, "FX/ShieldUpSFX", true);
            currentTween = TweenSXYZ.Add(
                sv.gameObject, 3f, shieldScale)
                    .EaseInOutCubic()
                    .WithScaledTime(true)
                    .Then(() => {
                        currentTween = null;
                        sv.localScale = Consts.V3One * shieldScale;
                        if (GameState.IsCurrent(S)) {
                            if (shieldVisuals == null || sv != shieldVisuals) {
                                Disable(true, sv);
                            }
                        }
                    });
        }

        public void Disable(bool instant) {
            Disable(instant, shieldVisuals);
        }

        private void Disable(bool instant, Transform sv) {
            // D.Warn("Disabling shield: {0}", instant);
            IsUp = false;
            if (sv == null) { return; }
            if (currentTween != null) {
                // currentTween.Skip();
                currentTween.enabled = false;
                GameObject.Destroy(currentTween);
                currentTween = null;
                instant = true;
            }
            var go = sv.gameObject;
            if (instant) {
                sv.localScale = Consts.V3Zero;
                PrefabPool.Despawn(go);
                shieldVisuals = null;
                return;
            }
            Sounds.PlayLoud(Center, "FX/ShieldDownSFX", true);
            currentTween = TweenSXYZ.Add(go, 3f, 0f)
                .EaseInOutCubic()
                .WithScaledTime(true)
                .Then(() => {
                    currentTween = null;
                    sv.localScale = Consts.V3Zero;
                    if (GameState.IsCurrent(S)) {
                        PrefabPool.Despawn(go);
                    }
                });
            shieldVisuals = null;
        }

        private float cameraShakeDuration;

        public void Update() {
            if (GameTimer.deltaTime == 0f) { return; }
            Vector3 shakeOffset;
            if (cameraShakeDuration > 0f) {
                shakeOffset = Rng.UInsideUnitCircle() * Mathf.Min(1f,
                    cameraShakeDuration);
                cameraShakeDuration -= GameTimer.unscaledDeltaTime;
            } else {
                shakeOffset = Consts.V3Zero;
            }
            if (shieldVisuals != null) {
                shieldVisuals.position = wantedPos + shakeOffset;
            }
        }

        public void Shake(float dur) {
            cameraShakeDuration = dur;
        }
    }
}
