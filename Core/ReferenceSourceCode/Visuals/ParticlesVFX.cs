using System;
using System.Collections;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using Game.Constants;
using UnityEngine.VFX;
using Game.Rendering;

namespace Game.Visuals {
    public class ParticlesVFX {
        [SelfInit]
        private static void Initialize() {
            isCosmeticParticlesEnabled = true;
            hasCheckedCosmeticParticles = false;
        }
        public GameObject GameObject => Fx == null ? null : Fx.gameObject;
        private VisualEffect Fx;
        private GameState S;
        public Entity Owner;
        private int budgetKey;
        public GameAudioSource Ambience;
        private Transform transform;
        private OwnedStationaryVisualEffect stationary;
        private OwnedDynamicVisualEffect dynamic;
        private Clock clock;
        private bool isSpawned;
        // FIXME figure out if we still need it
        private const float defaultParticleZ = -5;
        public bool IsDestroyed;
        private string prefabName;
        private static bool isCosmeticParticlesEnabled = true;
        private static bool hasCheckedCosmeticParticles;
        public bool TryGetFX(out VisualEffect fx) {
            fx = Fx;
            return fx != null;
        }

        public static ParticlesVFX CreateWithBudget(GameState S, string prefabName,
                Vector3 pos, int budgetKey, int budget, bool isCosmetic = true,
                int orderInLayer = 0) {
            CheckCosmeticParticles();
            // FIXME pool this
            var sys = new ParticlesVFX();
            if (RenderUtils.DisableAllVFX) { return sys; }
            if (ParticleBudget.IsVFXDisabledByTunable(prefabName)) { return sys; }
            if (isCosmeticParticlesEnabled || !isCosmetic) {
                // budget check
                var curCount = S.BB.Get(budgetKey, 0);
                if (curCount >= budget) {
                    return sys;
                }
                if (!ParticleBudget.TryAcquire(prefabName)) {
                    return sys;
                }
                sys.S = S;
                sys.prefabName = prefabName;
                S.BB.Set(budgetKey, curCount + 1);
                sys.budgetKey = budgetKey;
                CreateVFX(sys, prefabName, pos, true, orderInLayer);
            }
            return sys;
        }

        public static ParticlesVFX Create(string prefabName,
                Vector3 pos, bool autoPlay = true, bool isCosmetic = true,
                int orderInLayer = 0) {
            if (isCosmetic) {
                CheckCosmeticParticles();
            }

            // FIXME pool this
            var sys = new ParticlesVFX();
            if (RenderUtils.DisableAllVFX) { return sys; }
            if (ParticleBudget.IsVFXDisabledByTunable(prefabName)) { return sys; }
            if (isCosmeticParticlesEnabled || !isCosmetic) {
                if (!ParticleBudget.TryAcquire(prefabName)) {
                    return sys;
                }
                sys.prefabName = prefabName;
                CreateVFX(sys, prefabName, pos, autoPlay, orderInLayer);
            }
            return sys;
        }

        private static void CreateVFX(ParticlesVFX sys, string prefabName,
                Vector3 pos, bool autoPlay, int orderInLayer) {
            var prefab = ResourceLibrary.FindPrefab<VisualEffect>(prefabName);
            if (prefab == null) {
                D.Err("No prefab for Particles: {0}", prefabName);
                return;
            }
            sys.prefabName = prefabName;
            sys.Fx = PrefabPool.Spawn(prefab, new Vector3(pos.x, pos.y, defaultParticleZ),
                ResourceLibrary.Root);
            var rend = sys.Fx.GetComponent<Renderer>();
            rend.sortingOrder = orderInLayer;
            #if !PRODUCTION_BUILD
            sys.Fx.name = string.Format("ParticlesVFX: {0}", prefabName);
            #endif
            sys.transform = sys.Fx.transform;
            if (autoPlay) {
                sys.Fx.Play();
            }
            sys.isSpawned = true;
        }

        private static void CheckCosmeticParticles() {
            if (!hasCheckedCosmeticParticles) {
                hasCheckedCosmeticParticles = true;
                isCosmeticParticlesEnabled =
                    The.Prefs.GetBool(Pref.GCosmeticParticles,
                        PrefH.GCosmeticParticles, true);
            }
        }

        public ParticlesVFX WithDynamicOwnership(Entity owner, Func<Vector2> positionFunc) {
            if (!isCosmeticParticlesEnabled) { return this; }
            if (owner == null) {
                D.Err("Owner is null when trying to set dynamic ownership for vfx! {0}", this);
            }
            if (!isSpawned) { return this; }
            if (dynamic == null) {
                if (!Fx.TryGetComponent(out dynamic)) {
                    dynamic = Fx.gameObject
                        .AddComponent<OwnedDynamicVisualEffect>();
                    dynamic.VFX = this;
                }
            }
            if (!dynamic.enabled) {
                dynamic.enabled = true;
            }
            Owner = owner;
            dynamic.PositionFunc = positionFunc;
            return this;

        }

        public ParticlesVFX WithStaticOwnership(Entity owner) {
            if (!isCosmeticParticlesEnabled) { return this; }
            if (owner == null) {
                D.Err("Owner is null when trying to set static ownership for vfx! {0}", owner);
            }
            if (!isSpawned) { return this; }
            if (stationary == null) {
                if (!Fx.TryGetComponent(out stationary)) {
                    stationary = Fx.gameObject
                        .AddComponent<OwnedStationaryVisualEffect>();
                    stationary.VFX = this;
                }
            }
            if (!stationary.enabled) {
                stationary.enabled = true;
            }
            Owner = owner;
            if (owner != null) {
                stationary.OwnerStartPos = owner.PosIdx;
            }
            return this;
        }

        public ParticlesVFX WithoutOwnership() {
            Owner = null;
            if (stationary?.enabled == true) {
                stationary.enabled = false;
            }
            if (dynamic?.enabled == true) {
                dynamic.enabled = false;
            }
            return this;
        }

        public void Play(bool on) {
            if (!isSpawned) { return; }
            if (on) {
                Fx.Play();
            } else {
                Fx.Stop();
            }
        }

        public void DestroyAfterRealTime(double seconds) {
            if (G.ClockSpeed >= ClockSpeed.Unlimited) {
                Destroy();
            } else {
                Coroutines.Start(DestroyAfterSeconds(seconds, true),
                    this, "AutoDestroyParticleFX", false, true);
            }
        }
        public void DestroyAfterGameTime(double seconds) {
            // D.Warn("Destroying particles after game time: {0} | {1}", prefabName, seconds);
            if (G.ClockSpeed >= ClockSpeed.Unlimited) {
                Destroy();
            } else {
                Coroutines.Start(DestroyAfterSeconds(seconds, false),
                    this, "AutoDestroyParticleFX", false, true);
            }
        }

        private IEnumerator DestroyAfterSeconds(double seconds, bool realTime) {
            var r = The.R;
            if (realTime) {
                seconds += GameTimer.realtimeSinceStartupAsDouble;
            } else {
                seconds += GameTimer.time;
            }
            while (realTime ? GameTimer.realtimeSinceStartupAsDouble < seconds
                    : GameTimer.time < seconds) {
                yield return null;
                if (!r.IsCurrent) { yield break; }
            }
            Destroy();
        }

        public void Rotate(Quaternion rot) {
            if (!isSpawned) { return; }
            transform.rotation = rot;
        }

        public void MoveTo(Vector2 newPosition, float z = defaultParticleZ) {
            if (!isSpawned) { return; }
            transform.position = new(
                newPosition.x, newPosition.y, z);
        }

        public void Destroy() {
            if (IsDestroyed) { return; }
            // D.Warn("Destroying particles: {0}", prefabName);
            IsDestroyed = true;
            Fx?.Stop();
            WithoutOwnership();
            if (clock != null) {
                clock.OnSpeedChange.RemoveListener(OnClockSpeedChange);
                clock = null;
            }
            if (Ambience != null) {
                Ambience.Destroy();
                Ambience = null;
            }
            if (prefabName != null) {
                ParticleBudget.Release(prefabName);
            }
            CleanLists();
            if (budgetKey != 0) {
                if (S == null) {
                    D.Err("ParticleVFX has budgetKey but GameState is null! {0}",
                        this);
                } else {
                    S.BB.Decrement(budgetKey);
                }
                budgetKey = 0;
            }
        }

        public override string ToString() {
            if (Fx) {
                return string.Format("Particles: {0}", Fx.name);
            }
            return "Particles (uninitialized)";
        }

        public void CleanLists() {
            Owner = null;
            isSpawned = false;
            if (Fx != null) {
                PrefabPool.Despawn(Fx.gameObject);
                Fx = null;
            }
        }

        public void ScaleTo(float x) {
            if (!isSpawned) { return; }
            transform.localScale = Consts.V3One * x;
        }
        public void SetClock(Clock clock) {
            if (this.clock != null) {
                this.clock.OnSpeedChange.RemoveListener(OnClockSpeedChange);
            }
            this.clock = clock;
            clock.OnSpeedChange.AddListener(OnClockSpeedChange);
            OnClockSpeedChange(clock.Speed);
        }

        private void OnClockSpeedChange(ClockSpeed speed) {
            if (speed == ClockSpeed.Stopped) {
                SetSpeed(0);
            } else {
                SetSpeed(clock.FXTimeScale);
            }
        }

        private void SetSpeed(float speed) {
            if (!isSpawned) { return; }
            Fx.playRate = speed;
        }
    }
}
