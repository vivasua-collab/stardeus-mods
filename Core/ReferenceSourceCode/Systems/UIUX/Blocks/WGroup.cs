using System;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WGroup : WBase {
        public RectTransform Container;
        private void Awake() {
            if (Container == null) {
                D.Err("Container not set for WGroup! {0}", D.GOPath(transform));
                Container = GetComponent<RectTransform>();
            }
        }

        [Obsolete("Use lifetime-based management instead", error: true)]
        public void DespawnChildren() {
            var childCount = Container.childCount;
            for (int i = childCount - 1; i >= 0; i--) {
                var child = Container.GetChild(i);
                if (child.gameObject.activeSelf) {
                    if (child.TryGetComponent<WGroup>(out var wg)) {
                        wg.DespawnChildren();
                    }
                    D.Warn("Despawning child: {0}", D.GOPath(child.transform));
                    PrefabPool.Despawn(child.gameObject);
                }
            }
        }

        /// <summary>
        /// Never call this outside Awake or Initialize! Lifetime must be used
        /// to despawn widgets during normal loading / unloading / reloading
        /// of the panels!
        /// </summary>
        public void DestroyChildren() {
            MiscUtils.DestroyChildren(Container);
        }

        [ContextMenu("Rebuild Container Layout")]
        private void RebuildContainerLayout() {
            LayoutRebuilder.ForceRebuildLayoutImmediate(Container);
        }
        public override string ToString() {
            return "WGroup[]";
        }
    }
}
