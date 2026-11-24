using System.Collections;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using static KL.Pool.PrefabPool;

namespace Game.Systems.UIUX {
    public abstract class UIThemedBase : MonoBehaviour, IPreInstantiatable {
        [SerializeField] protected string configId = "Default";
        protected string configOverrideId;
        protected bool isDestroyed;
        protected bool wasApplied;

        public string EffectiveConfigId => configOverrideId ?? configId;

        protected void Awake() {
            if (!wasApplied && The.UITheme != null) {
                ApplyStyle();
            }
        }

        protected IEnumerator Start() {
            if (wasApplied) { yield break; }
            while (The.UITheme == null) {
                yield return null;
                if (isDestroyed) { yield break; }
            }
            ApplyStyle();
        }

        public void ApplyStyle() {
            ApplyStyleInternal(force: false);
        }

        [ContextMenu("Apply Style")]
        public void ForceApplyStyle() {
            ApplyStyleInternal(force: true);
        }

        protected void ApplyStyleInternal(bool force) {
            if (The.UITheme == null) {
                D.Err("Trying to apply theme while it's not loaded! (force: {0}) | {1}",
                    force, D.GOPath(transform));
                return;
            }
            if (!force && wasApplied) { return; }
            wasApplied = true;
            try {
                ApplyTheme();
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed applying style for: {0}", D.GOPath(transform));
            }
        }

        protected abstract void ApplyTheme();

        protected virtual void OnDestroy() {
            isDestroyed = true;
        }

        public void Override(string config) {
            if (config != configOverrideId && config != configId) {
                configOverrideId = config;
                ApplyStyleInternal(true);
            }
        }

        public void ClearOverride() {
            if (configOverrideId != null) {
                configOverrideId = null;
                ApplyStyleInternal(true);
            }
        }

        public void PreInstantiate() {
            if (The.UITheme != null) {
                ApplyStyle();
            } else {
                Ready.WhenCore(() => {
                    D.Warn("Applying delayed style to item: {0}", D.GOPath(transform));
                    ApplyStyle();
                });
            }
        }
    }
}
