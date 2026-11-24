#if UNITY_EDITOR
//#define DEBUG_VFX
#endif
using System;
using Game.Constants;
using Game.Rendering;
using Game.UI;
using KL.Pool;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace Game.Visuals {
    public sealed class VFXHolder : IVFXParticles {
        private static readonly int fxLayer = LayerMask.NameToLayer("TransparentFX");
        public VisualEffect VFX;
        public readonly string VFXName;
        public bool IsPlaying;
        private bool acquiredBudget;
        public bool IsSpawned;
        private bool hideInFullScreenWidget;
        public bool ReinitOnShow = true;
        private bool isScaled;
        private bool isPlayRateSet;

        // PrefabPool.Spawn only restores position and rotation, and the VisualEffect property
        // sheet lives on the pooled component, so anything we change here is inherited by
        // whatever VFX reuses this instance next. Track it and restore prefab defaults on Destroy.
        public void SetScale(float scale) {
            if (!IsSpawned || VFX == null) { return; }
            isScaled = true;
            VFX.transform.localScale = Consts.V3One * scale;
            VFX.SetFloat(ShaderVars.Scale, scale);
        }

        public void SetPlayRate(float rate) {
            if (!IsSpawned || VFX == null) { return; }
            isPlayRateSet = true;
            VFX.playRate = rate;
        }

        public void Stop() {
            if (!IsSpawned) { return; }
            if (!IsPlaying) {
                #if !PRODUCTION_BUILD
                if (VFX != null) {
                    D.Ass(!VFX.gameObject.activeInHierarchy,
                        "Not playing but VFX active! {0}", VFX.name);
                }
                #endif
                #if DEBUG_VFX
                D.Log("Trying to stop VFX while it was not playing! {0}", VFX.name);
                #endif
                return;
            }
            #if DEBUG_VFX
            D.Log("Stopping VFX: {0}", VFX.name);
            #endif
            IsPlaying = false;
            if (VFX != null) {
                VFX.Stop();
                VFX.SetActiveFast(false);
            }
        }

        public void Show() {
            if (!IsSpawned || VFX == null) { return; }
            if (!IsPlaying) {
                VFX.gameObject.SetActive(true);
            }
            // If we never reinit, fire becomes invisible after a few hours of playtime,
            // thus we force reinit if vfx wasn't playing (had 0 count)
            if (ReinitOnShow || !IsPlaying) {
                VFX.Reinit();
            }
            if (!IsPlaying) {
                #if DEBUG_VFX
                D.Log("Starting VFX: {0}", VFX.name);
                #endif
                if (hideInFullScreenWidget) {
                    VFX.SetFloat(ShaderVars.Visibility,
                        UIShowing.FullScreenWidget ? 0f : 1f);
                }
                VFX.Play();
                IsPlaying = true;
            }
        }

        public void ForceReinit() {
            if (!IsSpawned || VFX == null || !IsPlaying) { return; }
            VFX.Reinit();
        }

        public VFXHolder(string vfxName, string layerName = null, Transform parent = null,
                bool hideInFullScreenWidget = false) {
            VFXName = vfxName;
            if (ParticleBudget.IsVFXDisabledByTunable(vfxName)) {
                return;
            }
            if (!ParticleBudget.TryAcquire(vfxName)) {
                return;
            }
            acquiredBudget = true;
            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            if (prefab == null) {
                ParticleBudget.Release(vfxName);
                acquiredBudget = false;
                return;
            }
            parent ??= ResourceLibrary.Root;
            VFX = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId, parent);
            // The pool reparents keeping the world scale, and most effects simulate in local
            // space, so a scaled holder draws every particle bigger and further from the
            // origin: fire shards drew their flames off the map (STA-1854, FireSystem.md)
            var tr = VFX.transform;
            if (tr.localScale != Consts.V3One) {
                // Under a parent of its own the parent's scale shows up here legitimately
                if (parent == ResourceLibrary.Root && !warnedScaledFromPool) {
                    warnedScaledFromPool = true;
                    D.Warn("Pooled VFX {0} came back with scale {1}, reset", vfxName,
                        tr.localScale);
                }
                tr.localScale = Consts.V3One;
            }
            #if DEBUG_VFX
            if (!VFX.TryGetComponent<VFXDebugger>(out var dbg)) {
                VFX.gameObject.AddComponent<VFXDebugger>();
            }
            #endif

            if (layerName != null) {
                VFX.gameObject.layer = LayerMask.NameToLayer(layerName);
            } else {
                VFX.gameObject.layer = fxLayer;
            }
            VFX.gameObject.SetActive(false);
            if (layerName == null) {
                VFX.gameObject.name = $"VFXHolder_{vfxName}";
            } else {
                VFX.gameObject.name = $"VFXHolder_{vfxName}_{layerName}";
            }
            VFX.visualEffectAsset = Resources.Load<VisualEffectAsset>($"VFX/{vfxName}");
            if (VFX.visualEffectAsset == null) {
                D.Err("VFX asset not found by name: {0}", vfxName);
                VFX.visualEffectAsset = Resources.Load<VisualEffectAsset>("VFX/VFXMissingFallback");
            }
            this.hideInFullScreenWidget = hideInFullScreenWidget;
            if (hideInFullScreenWidget) {
                A.S.Sig.OnShowFullScreenWidget.AddListener(OnShowFSW);
            }
            IsSpawned = true;
        }

        private static bool warnedScaledFromPool;

        private void OnShowFSW(bool yes) {
            if (!IsSpawned) { return; }
            VFX.SetFloat(ShaderVars.Visibility, yes ? 0f : 1f);
        }

        private bool isQuitting;

        public string DebugInfo {
            get {
                return $"VFXHolder[{VFXName}|Playing:{IsPlaying}|VFXActive:{VFX?.isActiveAndEnabled}|VFXGOActive:{VFX?.gameObject.activeInHierarchy})";
            }
        }

        private void OnApplicationQuit() {
            isQuitting = true;
        }

        public void Destroy() {
            IsSpawned = false;
            VFX?.Stop();
            if (isQuitting) {
                if (acquiredBudget) {
                    ParticleBudget.Release(VFXName);
                    acquiredBudget = false;
                }
                return;
            }
            if (VFX != null && VFX.gameObject != null) {
                VFX.gameObject.layer = fxLayer;
                // Back under the pool root: a holder despawned under a scaled parent (a
                // starfield space object) handed that scale to the next effect
                var tr = VFX.transform;
                if (ResourceLibrary.Root != null && tr.parent != ResourceLibrary.Root) {
                    tr.SetParent(ResourceLibrary.Root, false);
                }
                tr.localScale = Consts.V3One;
                if (isScaled) {
                    isScaled = false;
                    VFX.ResetOverride(ShaderVars.Scale);
                }
                if (isPlayRateSet) {
                    isPlayRateSet = false;
                    VFX.playRate = 1f;
                }
                if (VFX.gameObject.activeSelf) {
                    PrefabPool.Despawn(VFX.gameObject);
                }
            }
            if (hideInFullScreenWidget) {
                A.S?.Sig?.OnShowFullScreenWidget.RemoveListener(OnShowFSW);
            }
            if (acquiredBudget) {
                ParticleBudget.Release(VFXName);
                acquiredBudget = false;
            }
            VFX = null;
        }

        public override string ToString() {
            return $"VFXHolder[{VFXName}|{fxLayer}|{VFX}]";
        }

        private void OnDestroy() {
            Destroy();
        }
    }
}
