using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ContextPopupWidget : BaseWidget, ICustomPoolable, ICloseableWidget {
        [SerializeField] private WButtonText wHeader;
        [SerializeField] private WGroup wContent;
        [SerializeField] private WGroup wFooter;
        [SerializeField] private UIHeightAdjuster heightAdjuster;
        private IContextPopup loader;
        private Lifetime lifetime;
        private void OnClose() {
            if (!A.S.UIQuery.ShowContextPopup.Ask(loader, false)) {
                D.Err("Failed closing context popup cleanly, will despawn: {0}", loader);
                PrefabPool.Despawn(gameObject);
            }
        }

        public bool Show(IContextPopup loader, bool wasSpawned) {
            this.loader = loader;
            int width = 420;
            if (loader is IContextPopupWithWidth withWidth) {
                width = withWidth.PopupWidth;
            }
            Rect.sizeDelta = new Vector2(width, Rect.sizeDelta.y);

            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("ContextPopup");
            var title = loader.LoadPopupContent(lifetime, wContent, wFooter);
            if (wasSpawned) {
                wHeader.Load(title, OnClose);
                loader.OnPopupOpened();
                var centerOfScreen = loader.ShowInScreenCenter;
                if (heightAdjuster != null) {
                    heightAdjuster.ClearAnchor();
                    if (!centerOfScreen && The.Bindings?.Mouse != null) {
                        heightAdjuster.SetMouseAnchorY(The.Bindings.Mouse.ScreenPosition.y);
                    }
                }
                Coroutines.OnFrame(1, () => {
                    if (MiscUtils.IsDestroyed(this) || loader == null) { return; }
                    UIUtils.RebuildLayoutRecursive(Rect);
                    // Decided a frame later: showing an overlay opens its legend before the
                    // overlay buttons it docks to
                    if (TryGetDock(loader, out var dockTopRight)) {
                        if (heightAdjuster != null) {
                            heightAdjuster.SetBottomAnchorY(dockTopRight.y);
                        }
                        UIUtils.AfterShowPopupAbove(Rect, dockTopRight);
                    } else {
                        UIUtils.AfterShowPopup(Rect, centerOfScreen, 0f, randShift: false);
                    }
                });
            } else {
                wHeader.SetText(title);
            }
            Rect.SetAsLastSibling();
            return true;
        }

        private const float DockGap = 8f;

        private bool TryGetDock(IContextPopup popup, out Vector3 dockTopRight) {
            dockTopRight = default;
            if (popup is not IOverlayLegendPopup) { return false; }
            if (Registries.UIWidgets.Get(UIWidgetId.BottomBar, false) is not BottomBarWidget bar) {
                return false;
            }
            if (!bar.TryGetOverlaysDock(out dockTopRight)) { return false; }
            dockTopRight.y += DockGap * Rect.lossyScale.y;
            return true;
        }

        public void PoolDeactivate() {
            if (gameObject.activeSelf) {
                gameObject.SetActiveFast(false);
                // do it here to prevent infinite loop in some OnPopupClosed calls
                loader?.OnPopupClosed();
            }
            loader = null;
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        public void PoolActivate() {
            gameObject.SetActiveFast(true);
        }

        public bool TryCloseWidget() {
            if (gameObject.activeSelf) {
                OnClose();
                return true;
            }
            return false;
        }
    }
}
