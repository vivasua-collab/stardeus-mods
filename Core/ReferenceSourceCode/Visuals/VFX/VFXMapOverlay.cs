#if !PRODUCTION_BUILD
// #define DEBUG_VFX
#endif
using System;
using System.Collections;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace Game.Visuals {
    public sealed class VFXMapOverlay : IVFXParticles {
        private VisualEffect effect;
        private bool isPlaying;
        private string vfxName;
        private bool acquiredBudget;
        private bool isSpawned;
        public void PhaseOut(float delay) {
            if (!isSpawned) {
                Destroy();
                return;
            }
            if (G.ClockSpeed >= ClockSpeed.Unlimited) {
                #if DEBUG_VFX
                D.Warn("Phasing out Overlay VFX (instant): {0}", this);
                #endif
                Stop();
                Destroy();
            } else {
                Coroutines.Start(DoPhaseOut(delay), this,
                    "StopAndDespawnVFX", false, false);
            }
        }

        private IEnumerator DoPhaseOut(float delay) {
            #if DEBUG_VFX
            D.Warn("Phasing out Overlay VFX: {0} | {1}", delay, this);
            #endif
            var s = The.R.State;
            if (!GameState.IsCurrent(s)) { yield break; }
            float time = GameTimer.time;
            float start = time;
            float end = start + delay;
            while (time < end)  {
                float visibility = Mathf.InverseLerp(end, start, time);
                effect.SetFloat(ShaderVars.Visibility, visibility);
                yield return null;
                time = GameTimer.time;
                if (!GameState.IsCurrent(s)) { yield break; }
            }
            Stop();
            Destroy();
        }

        public void Stop() {
            if (!isSpawned) { return; }
            if (!isPlaying) { return; }
            #if DEBUG_VFX
            D.Warn("Stopping Overlay VFX: {0}", this);
            #endif

            isPlaying = false;
            effect.Stop();
            effect.gameObject.SetActive(false);
        }

        public void Show() {
            if (!isSpawned) { return; }
            #if DEBUG_VFX
            D.Warn("Showing Overlay VFX: {0}", this);
            #endif
            if (!isPlaying) {
                effect.gameObject.SetActive(true);
            }
            effect.SetFloat(ShaderVars.Visibility, 1f);
            effect.Reinit();
            if (!isPlaying) {
                effect.Play();
                isPlaying = true;
            }
        }

        public VFXMapOverlay(string vfxName, Vector4 bounds) {
            this.vfxName = vfxName;
            if (ParticleBudget.IsVFXDisabledByTunable(vfxName)) {
                return;
            }
            if (!ParticleBudget.TryAcquire(vfxName)) {
                return;
            }
            acquiredBudget = true;
            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            effect = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            effect.gameObject.SetActive(false);
            effect.gameObject.name = $"VFXHolder_{vfxName}";
            effect.visualEffectAsset = Resources.Load<VisualEffectAsset>(
                $"VFX/{vfxName}");
            effect.SetVector4(ShaderVars.Viewport, bounds);
            isSpawned = true;
        }

        public void Destroy() {
            isSpawned = false;
            #if DEBUG_VFX
            D.Warn("Destroying Overlay VFX: {0}", this);
            #endif
            if (effect != null) {
                effect.Stop();
                PrefabPool.Despawn(effect.gameObject, onExit: true);
                effect = null;
            }
            if (acquiredBudget) {
                ParticleBudget.Release(vfxName);
                acquiredBudget = false;
            }
        }
    }
}
