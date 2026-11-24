#if !PRODUCTION_BUILD
// #define DEBUG_LIFETIME
#endif
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public abstract class WBase : MonoBehaviour, IWithLifetime {
        protected RectTransform rect;
        public RectTransform Rect => rect ??= GetComponent<RectTransform>();
        private PrefabPool.PoolMember poolMember;
        protected virtual bool IsPoolable => true;
        public bool IsDisposed {
            get {
                if (poolMember == null) {
                    if (!IsPoolable) {
                        return !gameObject.activeInHierarchy;
                    }
                    if (MiscUtils.IsDestroyed(this)) {
                        D.Err("Getting IsDisposed on a destroyed obj");
                        return true;
                    }
                    if (!TryGetComponent(out poolMember)) {
                        #if !PRODUCTION_BUILD
                        // It's OK to get this in pinned quick search results
                        D.Err("Checking if a widget is disposed while it has no pool member: {0}",
                            D.GOPath(transform));
                        #endif
                        return !gameObject.activeSelf;
                        // return false;
                    }
                }
                #if DEBUG_LIFETIME
                D.Log("WBase.IsDisposed check for {0}: {1}", D.GOPath(transform), !poolMember.IsSpawned);
                #endif
                return !poolMember.IsSpawned;
            }
        }

        public void SetSiblingIdx(int idx) {
            Rect.SetSiblingIndex(idx);
        }

        public void SetActive(bool yes) {
            gameObject.SetActiveFast(yes);
        }

        [ContextMenu("RebuildLayout")]
        public void RebuildLayout() {
            if (MiscUtils.IsDestroyed(this)) { return; }
            UIUtils.RebuildLayoutRecursive(Rect);
        }

        public void Dispose() {
            #if DEBUG_LIFETIME
            D.Log("WBase.Dispose called for {0}", D.GOPath(transform));
            #endif
            PrefabPool.Despawn(gameObject);
        }
    }
}
