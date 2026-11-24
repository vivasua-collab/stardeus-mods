using System;
using System.Collections;
using Game.Data;
using Game.Utils;
using KL.Clock;
using KL.Pool;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Game.Constants;

namespace Game.Visuals {
    public class Particles {
        public GameObject GameObject => Fx == null ? null : Fx.gameObject;
        public ParticleSystem Fx;
        public Entity Owner;
        private Transform transform;
        private Clock clock;
        // FIXME figure out if we still need it
        private const float defaultParticleZ = -5;
        private int frameRange;
        private UpdateHook hook;
        private bool isSpawned;
        public bool IsDestroyed;
        private string prefabName;
        public static Particles Create(string prefabName,
                uint seed, Vector3 pos,
                Action onUpdate = null, bool autoplay = true) {
            var sys = new Particles();
            if (RenderUtils.DisableAllVFX) {
                return sys;
            }
            if (ParticleBudget.IsVFXDisabledByTunable(prefabName)) {
                return sys;
            }
            if (!ParticleBudget.TryAcquire(prefabName)) {
                return sys;
            }
            var prefab = ResourceLibrary.FindPrefab<ParticleSystem>(prefabName);
            if (prefab == null) {
                D.Err("No prefab for Particles: {0}", prefabName);
                ParticleBudget.Release(prefabName);
                return null;
            }
            sys.prefabName = prefabName;
            sys.Fx = PrefabPool.Spawn(prefab, new Vector3(pos.x, pos.y, defaultParticleZ),
                ResourceLibrary.Root);
            if (onUpdate != null) {
                if (sys.hook == null) {
                    sys.hook = sys.Fx.gameObject.AddComponent<UpdateHook>();
                }
                sys.hook.OnUpdate = onUpdate;
                sys.hook.enabled = true;
            }
            sys.Fx.name = string.Format("Particles: {0}", prefabName);
            if (seed != 0) {
                sys.Fx.randomSeed = seed;
            }
            sys.transform = sys.Fx.transform;
            if (autoplay) {
                sys.Fx.Play();
            }
            sys.isSpawned = true;
            return sys;
        }

        public void Play(bool on) {
            if (!isSpawned) { return; }
            if (on) {
                Fx.Play();
            } else {
                Fx.Stop();
            }
        }

        public void AddSprite(Sprite sprite) {
            if (!isSpawned) {
                // D.Warn("Trying to set a texture sheet on Particles when fx is null");
                return;
            }
            var anim = Fx.textureSheetAnimation;
            anim.AddSprite(sprite);
        }

        public void SetFrame(float percent) {
            if (!isSpawned) {
                // D.Warn("Trying to set a frame on Particles when fx is null");
                return;
            }
            var frames = Fx.textureSheetAnimation;
            if (frameRange == 0) {
                frameRange = frames.spriteCount - 1;
                if (frameRange < 1) {
                    // Eager load it if it's not set yet
                    Fx.GetComponent<DynamicParticleSpriteSheet>().Load();
                }
            }
            frames.frameOverTime = new(
                percent * frameRange);
        }

        public void DestroyAfter(float seconds) {
            if (G.ClockSpeed >= ClockSpeed.Unlimited) {
                Destroy();
            } else {
                Coroutines.Start(DestroyAfterSeconds(seconds),
                    this, "AutoDestroyParticleFX", false, true);
            }
        }

        private IEnumerator DestroyAfterSeconds(float seconds) {
            var r = The.R;
            var until = GameTimer.realtimeSinceStartupAsDouble + seconds;
            while (GameTimer.realtimeSinceStartupAsDouble < until) {
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
            transform.position = new Vector3(
                newPosition.x, newPosition.y, z);
        }
        public void MoveBy(Vector3 delta) {
            if (!isSpawned) { return; }
            transform.position += delta;
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
            var main = Fx.main;
            if (main.useUnscaledTime) {
                // These ignore Time.timeScale, so Pause()/Play() is the only way to
                // freeze/resume them in step with clock speed.
                if (speed == 0) {
                    if (!Fx.isStopped) {
                        Fx.Pause();
                    }
                } else {
                    if (!Fx.isStopped) {
                        Fx.Play();
                    }
                    main.simulationSpeed = speed;
                }
                return;
            }
            // Everything else already freezes/scales via Time.timeScale (set to
            // FXTimeScale by Clock.SetTimeScale). Also toggling Pause()/Play() here
            // was redundant, and during a winch - which forces the clock to Stopped
            // for its entire, potentially multi-second, duration - that redundant
            // pause/resume cycle left already-alive particles rendering stuck at
            // their pre-pause position once their transform moved again afterward.
            if (!Fx.isStopped && speed > 0) {
                main.simulationSpeed = speed;
            }
        }

        public void Destroy() {
            if (IsDestroyed) { return; }
            IsDestroyed = true;
            if (hook != null) {
                hook.OnUpdate = null;
                hook.enabled = false;
            }
            if (clock != null) {
                clock.OnSpeedChange.RemoveListener(OnClockSpeedChange);
                clock = null;
            }
            if (Fx?.isPlaying == true) {
                Fx.Stop();
            }
            Owner = null;
            if (prefabName != null) {
                ParticleBudget.Release(prefabName);
            }
            CleanLists();
        }

        public override string ToString() {
            if (Fx) {
                return string.Format("Particles: {0}", Fx.name);
            }
            return "Particles (uninitialized)";
        }

        public void CleanLists() {
            isSpawned = false;
            if (Fx != null) {
                PrefabPool.Despawn(Fx.gameObject);
                Fx = null;
            }
        }

        public void SetLifeTime(float v) {
            if (!isSpawned) { return; }
            var main = Fx.main;
            main.startLifetime = v;
        }

        public void ScaleTo(float x) {
            if (!isSpawned) { return; }
            transform.localScale = Consts.V3One * x;
        }
    }
}
