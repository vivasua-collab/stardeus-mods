using System;
using Game.Constants;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class PanelContainerWidget : BaseWidget {
        public void Initialize() {
            Register(UIWidgetId.PanelContainer, this);
            #if !PRODUCTION_BUILD
            foreach (Transform child in transform) {
                if (!child.TryGetComponent<ScalableUIElement>(out _)) {
                    D.Err("Child {0} of PanelContainerWidget is missing ScalableUIElement component!",
                        D.GOPath(child));
                }
                if (!child.TryGetComponent<UIBringToTopOnClick>(out _)) {
                    D.Err("Child {0} of PanelContainerWidget is missing UIBringToTopOnClick component!",
                        D.GOPath(child));
                }
            }
            #endif
        }

        #if UNITY_EDITOR
        private void Update() {
            foreach (Transform child in transform) {
                if (!child.TryGetComponent<ScalableUIElement>(out _)) {
                    D.Err("Child {0} of PanelContainerWidget is missing ScalableUIElement component!",
                        D.GOPath(child));
                }
            }
        }
        #endif

        public bool TryCloseTopmost() {
            var childCount = Rect.childCount;
            for (int i = childCount - 1; i >= 0; i--) {
                var tr = Rect.GetChild(i);
                if (tr.gameObject.activeSelf) {
                    if (tr.TryGetComponent<ICloseableWidget>(out var closeable)) {
                        if (tr.gameObject.TryGetComponent<CanvasGroup>(out var cg)) {
                            if (cg.alpha < 0.1f) {
                                continue; // skip invisible panels
                            }
                        }
                        if (closeable.TryCloseWidget()) {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool TryFocusSearchField() {
            if (UIShowing.FullScreenWidget) {
                return false;
            }
            var childCount = Rect.childCount;
            for (int i = childCount - 1; i >= 0; i--) {
                var tr = Rect.GetChild(i);
                if (tr.gameObject.activeSelf) {
                    if (tr.TryGetComponent<IWithSearch>(out var withSearch)) {
                        if (withSearch.TryFocusSearch()) {
                            tr.SetAsLastSibling();
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
