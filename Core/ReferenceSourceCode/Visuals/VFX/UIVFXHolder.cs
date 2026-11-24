#if UNITY_EDITOR
//#define DEBUG_VFX
#endif
using Game.Constants;
using Game.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;

namespace Game.Visuals {
    public sealed class UIVFXHolder : IVFXParticles {
        public VisualEffect VFX;
        public readonly string VFXName;
        public bool IsPlaying;
        public bool ReinitOnShow = true;

        private bool hideInFullScreenWidget;
        private GameObject rootGO;
        private RectTransform rootRect;
        private VfxToRawImage vfxToRawImage;

        public void SetSize(Vector2 size) {
            if (rootRect != null) {
                // let the visual effect go double the size so it can go out of bounds of the object
                rootRect.sizeDelta = size * 2f;
            }
        }

        public void Stop() {
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
                VFX.gameObject.SetActive(false);
            }
        }

        public void Show() {
            if (!IsPlaying) {
                if (VFX == null) {
                    return;
                }
                VFX.gameObject.SetActive(true);
            }
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

        public UIVFXHolder(string vfxName, Transform parent, Vector2 size, int vfxLayer,
                float orthographicSize = 100f, bool hideInFullScreenWidget = false) {
            VFXName = vfxName;
            this.hideInFullScreenWidget = hideInFullScreenWidget;

            // Create root GameObject with RawImage for rendering
            rootGO = new GameObject($"UIVFXHolder_{vfxName}");
            rootGO.transform.SetParent(parent, false);
            rootRect = rootGO.AddComponent<RectTransform>();
            // rootRect.anchorMin = Vector2.zero;
            // rootRect.anchorMax = Vector2.one;
            // rootRect.offsetMin = Vector2.zero;
            // rootRect.offsetMax = Vector2.one;
            SetSize(size);

            var rawImage = rootGO.AddComponent<RawImage>();
            rawImage.raycastTarget = false;

            // Create VfxToRawImage component
            vfxToRawImage = rootGO.AddComponent<VfxToRawImage>();
            vfxToRawImage.Initialize(rawImage, vfxLayer, orthographicSize);

            // Create VFX GameObject
            var vfxGO = new GameObject($"UIVFXHolder_{vfxName}_Effect");
            vfxGO.transform.SetParent(rootGO.transform, false);
            vfxGO.layer = vfxLayer;
            VFX = vfxGO.AddComponent<VisualEffect>();

            VFX.visualEffectAsset = Resources.Load<VisualEffectAsset>($"VFX/{vfxName}");
            if (VFX.visualEffectAsset == null) {
                D.Err("VFX asset not found by name: {0}", vfxName);
                VFX.visualEffectAsset = Resources.Load<VisualEffectAsset>("VFX/VFXMissingFallback");
            }

            VFX.gameObject.SetActive(false);
            vfxToRawImage.SetVisualEffect(VFX);

            if (hideInFullScreenWidget) {
                A.S.Sig.OnShowFullScreenWidget.AddListener(OnShowFSW);
            }
        }

        private void OnShowFSW(bool yes) {
            if (VFX != null) {
                VFX.SetFloat(ShaderVars.Visibility, yes ? 0f : 1f);
            }
        }

        public string DebugInfo {
            get {
                return $"UIVFXHolder[{VFXName}|Playing:{IsPlaying}|VFXActive:{VFX?.isActiveAndEnabled}|VFXGOActive:{VFX?.gameObject.activeInHierarchy})";
            }
        }

        public void Destroy() {
            VFX?.Stop();
            if (hideInFullScreenWidget) {
                A.S?.Sig?.OnShowFullScreenWidget.RemoveListener(OnShowFSW);
            }
            if (rootGO != null) {
                Object.Destroy(rootGO);
            }
            rootGO = null;
            rootRect = null;
            VFX = null;
            vfxToRawImage = null;
            IsPlaying = false;
        }

        public override string ToString() {
            return $"UIVFXHolder[{VFXName}|{VFX}]";
        }
    }
}
