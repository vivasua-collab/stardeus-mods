using System.Collections;
using Game.Data;
using Game.Utils;
using KL.Clock;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using Game.Constants;

namespace Game.Visuals {
    /// <summary>
    /// Robot trails visual effect
    /// </summary>
    public class Trails {
        public GameObject GameObject => fx == null ? null : fx.gameObject;
        private TrailRenderer fx;
        public Entity Owner;
        private Transform transform;
        // FIXME figure out if we still need it
        private const float defaultParticleZ = -5;
        private UpdateHook hook;
        public bool IsDestroyed;
        public static Trails Create(string prefabName, Vector3 pos) {
            var sys = new Trails();
            var prefab = ResourceLibrary.FindPrefab<TrailRenderer>(prefabName);
            D.Ass(prefab != null, "No prefab for {0}", prefabName);
            sys.fx = PrefabPool.Spawn(prefab, new(pos.x, pos.y, defaultParticleZ),
                ResourceLibrary.Root);
            sys.fx.Clear();
            sys.fx.name = string.Format("Trails: {0}", prefabName);
            sys.transform = sys.fx.transform;
            return sys;
        }

        public void DestroyAfter(GameState s, float seconds) {
            if (G.ClockSpeed >= ClockSpeed.Unlimited) {
                Destroy();
            } else {
                Coroutines.Start(DestroyAfterSeconds(s, seconds), this,
                    "AutoDestroyTrailFX", false, true);
            }
        }

        private IEnumerator DestroyAfterSeconds(GameState s, float seconds) {
            yield return s.WaitForSecondsGameTime(seconds);
            if (!GameState.IsCurrent(s)) { yield break; }
            Destroy();
        }

        public void MoveTo(Vector2 newPosition, float z = defaultParticleZ) {
            //fx.AddPosition(new Vector3(pos.x, pos.y, defaultParticleZ));
            transform.position = new(
                newPosition.x, newPosition.y, z);
        }

        public void Destroy() {
            if (IsDestroyed) { return; }
            IsDestroyed = true;
            if (hook != null) {
                hook.OnUpdate = null;
                hook.enabled = false;
            }
            Owner = null;
            CleanLists();
        }

        public override string ToString() {
            if (fx) {
                return string.Format("Trails: {0}", fx.name);
            }
            return "Trails (uninitialized)";
        }

        public void Play(bool on) {
            fx.emitting = on;
        }

        public void CleanLists() {
            if (fx != null) {
                PrefabPool.Despawn(fx.gameObject);
                fx = null;
            }
        }

        public void ScaleTo(float x) {
            transform.localScale = Consts.V3One * x;
        }
    }
}
