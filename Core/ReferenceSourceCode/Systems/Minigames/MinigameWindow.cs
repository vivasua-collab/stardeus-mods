using System;
using Game.Constants;
using Game.Data;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Minigames {
    // Owns the shared WGameApp widget and the minigame RenderTexture.
    // All resources are released through life.OnDispose, which is the single
    // teardown path that also runs during Lifetime.DisposeAll (before systems
    // unload when exiting to the main menu).
    public sealed class MinigameWindow {
        public const int RtWidth = 320;
        public const int RtHeight = 240;

        private Lifetime life;
        private WGameApp widget;
        private RenderTexture rt;
        private Action onClose;
        private Action onRestart;

        public RenderTexture Target => rt;
        public bool IsOpen => life != null && !life.IsDisposed;

        // True only when the window can receive game input: alive, spawned,
        // topmost among the panel container children and with no blocking
        // fullscreen / modal UI on top. IsLastSibling alone is not enough:
        // QuickSearch is a separate widget, not a PanelContainer child.
        public bool IsFocused {
            get {
                if (widget == null || !widget.isActiveAndEnabled || !IsOpen) {
                    return false;
                }
                if (!widget.Rect.IsLastSibling()) {
                    return false;
                }
                if (UIShowing.AnyOf(UIShowingFlags.MainMenu | UIShowingFlags.Dialog
                        | UIShowingFlags.FullScreenWidget | UIShowingFlags.QuickSearch
                        | UIShowingFlags.PanelModal)) {
                    return false;
                }
                return true;
            }
        }

        public void Open(GameState s, string title, Action onClose, Action onRestart) {
            this.onClose = onClose;
            this.onRestart = onRestart;
            life = Lifetime.Create("MinigameWindow");
            widget = UISpawn.Widget<WGameApp>(life, UIPrefabId.WGameApp,
                s.UIQuery.GetWidgetRegistry.Ask().PanelContainer.Rect, -1);
            rt = new RenderTexture(RtWidth, RtHeight, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                name = "MinigameRT",
                filterMode = FilterMode.Point,
            };
            rt.Create();
            The.Alloc.Register(rt);
            life.OnDispose(ReleaseRT);
            // Spawned once per window lifetime under the same Lifetime as the
            // widget (pooled despawn together); only visibility toggles after.
            var restartBtn = UISpawn.Widget<WButtonText>(life,
                UIPrefabId.WPanelButton_Confirm, widget.Footer.Rect, -1);
            restartBtn.Load(T.RestartNow, OnRestartClick);
            widget.RawImage.Load(rt);
            widget.Load(title, onClose);
            ShowFooter(false);
            widget.Rect.SetAsLastSibling();
        }

        private void OnRestartClick() {
            onRestart?.Invoke();
        }

        public void SetGameOver(bool over) {
            ShowFooter(over);
        }

        private void ShowFooter(bool yes) {
            if (widget != null) {
                widget.ShowFooter(yes);
            }
        }

        public void SetTitle(string title) {
            if (widget == null) { return; }
            widget.Load(title, onClose);
        }

        public void SetAsLastSibling() {
            if (widget != null) {
                widget.Rect.SetAsLastSibling();
            }
        }

        public void Close() {
            if (life == null) {
                widget = null;
                rt = null;
                return;
            }
            if (!life.IsDisposed) {
                if (widget != null) {
                    widget.RawImage.Load((RenderTexture) null);
                }
                life.Dispose();
            }
            life = null;
            widget = null;
            rt = null;
        }

        // Release strictly before Unregister - Alloc.Unregister errors when the
        // texture is still created (Alloc.Validate logs leaks on exit otherwise).
        private void ReleaseRT() {
            if (rt == null) { return; }
            if (rt.IsCreated()) {
                rt.Release();
            }
            The.Alloc.Unregister(rt);
            GameObject.Destroy(rt);
            rt = null;
        }
    }
}
