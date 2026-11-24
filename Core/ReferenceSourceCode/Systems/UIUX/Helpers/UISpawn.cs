using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public static class UISpawn {
        public static T WidgetPermanent<T>(T prefab, RectTransform parent) where T : WBase {
            if (prefab == null) {
                D.Throw("Trying to spawn a widget with prefab that does not exist in parent: {0}",
                    D.GOPath(parent));
            }
            var w = GameObject.Instantiate(prefab, parent);
            w.name = $"{prefab.name}_PERM";
            PrefabPool.Pool.PreInstantiate(w.gameObject);

            return w;
        }
        public static T WidgetPermanent<T>(string prefabId, RectTransform parent) where T : WBase {
            var prefab = ResourceLibrary.FindPrefab<T>(prefabId);
            if (prefab == null) {
                D.Throw("Trying to spawn a widget with prefab that does not exist: {0}. in parent: {1}",
                    prefabId, D.GOPath(parent));
            }
            return WidgetPermanent<T>(prefab, parent);
        }
        public static T WidgetPermanent<T>(RectTransform parent) where T : WBase {
            var prefab = ResourceLibrary.FindPrefab<T>();
            if (prefab == null) {
                D.Throw("Trying to spawn a widget with prefab that does not exist: {0}. in parent: {1}",
                    typeof(T), D.GOPath(parent));
            }
            return WidgetPermanent<T>(prefab, parent);
        }
        public static T Widget<T>(Lifetime lifetime, RectTransform parent) where T : WBase {
            var prefab = ResourceLibrary.FindPrefab<T>();
            if (prefab == null) {
                D.Throw("Trying to spawn a widget with prefab that does not exist: {0}. in parent: {1}",
                    typeof(T), D.GOPath(parent));
            }
            return Widget(lifetime, prefab, parent, -1);
        }
        public static T Widget<T>(Lifetime lifetime, string prefabId, RectTransform parent, int siblingIdx) where T : WBase {
            var prefab = ResourceLibrary.FindPrefab<T>(prefabId);
            if (prefab == null) {
                D.Throw("Trying to spawn a widget with prefab that does not exist: {0} ({1}). in parent: {2}",
                    prefabId, typeof(T), D.GOPath(parent));
            }
            return Widget(lifetime, prefab, parent, siblingIdx);
        }
        public static T Widget<T>(Lifetime lifetime, T prefab, RectTransform parent, int siblingIdx) where T : WBase {
            if (prefab == null) {
                D.Throw("Trying to spawn a widget with prefab that does not exist: {0}. in parent: {1}",
                    typeof(T), D.GOPath(parent));
            }
            if (lifetime == null || lifetime.IsDisposed) {
                // Fail before spawning, not after - otherwise we'd leak an active, untracked
                // pooled widget that never gets despawned.
                D.Throw("Trying to spawn widget {0} with a null/disposed lifetime. in parent: {1}",
                    typeof(T), D.GOPath(parent));
            }
            var w = PrefabPool.Spawn(prefab, parent, preInstantiate: true);
            // force the scale of 1
            var sc = w.Rect.localScale.x;
            if (sc != 1f) {
                if (!prefab.TryGetComponent<ScalableUIElement>(out _)) {
                    if (prefab.TryGetComponent<UISpaceObject>(out _)) {
                        // it's ok
                    } else {
                        if (prefab.transform.localScale.x != 1f) {
                            D.Err("Prefab has non-one scale! {0}", prefab.name);
                        }
                        // Space objects sit in the zoom-scaled starmap and scale themselves against
                        // the zoom, and a despawned one keeps its stale scale. A pooled widget moved
                        // from one into another inherits the scale ratio, so fixing it is expected.
                        // Inactive included: the starmap rebuilds before its widget is shown, and
                        // zoomed out icon rows are hidden
                        if (!Maths.Approximately(sc, 1f)
                                && parent.GetComponentInParent<UISpaceObject>(true) == null) {
                            D.Err("Fixing scale for {0} from {1} to one", prefab.name, sc);
                        }
                        w.Rect.localScale = Consts.V3One;
                    }
                }
            }
            if (siblingIdx >= 0) {
                w.SetSiblingIdx(siblingIdx);
            }
            lifetime.Add(w);
            return w;
        }

        public static WContentBlock Block(Lifetime lifetime, WGroup group,
                int siblingIdx = 0, string prefab = UIPrefabId.WContentBlock) {
            return Widget<WContentBlock>(lifetime, prefab,
                group.Container, siblingIdx);
        }
    }
}
