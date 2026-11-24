using System;
using System.Collections.Generic;
using Game.Data;
using Game.UI;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ContextPopupHandler : BaseWidgetHandler<ContextPopupWidget> {
        private readonly Dictionary<IContextPopup, ContextPopupWidget> instances = new();
        private readonly List<IContextPopup> tempPopups = new();
        public ContextPopupHandler(GameState s, ContextPopupWidget prefab) : base(s, prefab) {
            s.UIQuery.ShowContextPopup.SetResponder(OnShowPopup);
            s.UIQuery.ShowSystemContextPopup.SetResponder(OnShowSystemPopup);
            s.UIQuery.CloseContextPopup.SetResponder(OnClosePopup);
        }

        private bool OnClosePopup(IContextPopup loader) {
            if (loader.UniquePopupHash == 0) {
                D.Err("can't close non-unique popups for IContextPopup: {0}", loader);
                return false;
            }
            var h = loader.UniquePopupHash;
            tempPopups.Clear();
            foreach (var inst in instances.Keys) {
                if (inst.UniquePopupHash == h) {
                    tempPopups.Add(inst);
                }
            }
            foreach (var p in tempPopups) {
                var instance = instances[p];
                instances.Remove(p);
                if (!instance.IsDisposed) {
                    instance.Dispose();
                }
            }
            bool closed = tempPopups.Count > 0;
            tempPopups.Clear();
            return closed;
        }

        private bool OnShowSystemPopup(IContextPopup loader, bool show) {
            return DoShowPopup(loader, show, UIPoolRoot.SystemPopupContainer);
        }

        private bool OnShowPopup(IContextPopup loader, bool show) {
            return DoShowPopup(loader, show, UIPoolRoot.PopupContainer);
        }

        private bool DoShowPopup(IContextPopup loader, bool show, RectTransform container) {
            if (loader == null) {
                if (show) {
                    D.Err("IContextPopupLoader is null when trying to show!");
                    return false;
                } else {
                    if (instances.Count > 0) {
                        Cleanup();
                        return true;
                    }
                    return false;
                }
            }
            CleanupUnique(loader, show);
            bool wasSpawned = false;
            if (!instances.TryGetValue(loader, out var instance)) {
                if (!show) { return false; }
                instance = PrefabPool.Spawn(widget, container, preInstantiate: true);
                instances[loader] = instance;
                wasSpawned = true;
            }
            if (show) {
                return instance.Show(loader, wasSpawned);
            } else {
                instances.Remove(loader);
                if (!instance.IsDisposed) {
                    instance.Dispose();
                }
                return true;
            }
        }

        private void CleanupUnique(IContextPopup loader, bool show) {
            var h = loader.UniquePopupHash;
            if (show && h != 0) {
                tempPopups.Clear();
                foreach (var inst in instances.Keys) {
                    if (inst != loader && inst.UniquePopupHash == h) {
                        tempPopups.Add(inst);
                    }
                }
                foreach (var p in tempPopups) {
                    PrefabPool.Despawn(instances[p].gameObject);
                    instances.Remove(p);
                }
                tempPopups.Clear();
            }
        }

        public void Cleanup() {
            foreach (var inst in instances) {
                PrefabPool.Despawn(inst.Value.gameObject);
            }
            instances.Clear();
        }
    }
}
