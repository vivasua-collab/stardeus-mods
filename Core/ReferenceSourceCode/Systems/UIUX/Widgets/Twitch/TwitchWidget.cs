using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class TwitchWidget : BaseWidget, ICloseableWidget, IWithSearch {
        [SerializeField] private WGroup leftGroup;
        [SerializeField] private WGroup rightGroup;
        [SerializeField] private WGroup footerGroup;
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private WTabRow wTabRow;
        [SerializeField] private WInput wSearch;

        public WGroup LeftGroup => leftGroup;
        public WGroup RightGroup => rightGroup;
        public WGroup FooterGroup => footerGroup;
        public WButtonText PanelHeader => wPanelHeader;

        public Action<Lifetime> OnShow;
        private Lifetime lifetime;
        private readonly Dictionary<string, WTabButton> tabs = new();

        public void Initialize(GameState s, TwitchContext ctx, Action<string> onSearch) {
            wPanelHeader.Load(T.TwitchIntegration, Close);
            UIUtils.LoadSearch(wSearch, onSearch, placeholder: T.SearchFor);
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Twitch, this);
            wTabRow.DestroyChildren();
            leftGroup.DestroyChildren();
            rightGroup.DestroyChildren();
            footerGroup.DestroyChildren();
            CreateTabs(ctx);
        }

        private void CreateTabs(TwitchContext ctx) {
            tabs.Clear();
            int idx = 0;
            CreateTab(idx++, TwitchTabId.Viewer, ctx);
            CreateTab(idx++, TwitchTabId.Queue, ctx);
            CreateTab(idx++, TwitchTabId.Connection, ctx);
            CreateTab(idx++, TwitchTabId.Roles, ctx);
            CreateTab(idx++, TwitchTabId.Commands, ctx);
            CreateTab(idx++, TwitchTabId.Management, ctx);
        }

        private void CreateTab(int idx, string name, TwitchContext ctx) {
            var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var w = PrefabPool.Spawn(prefab, wTabRow.Container);
            w.Load(name, () => ctx.ShowTab(name));
            w.SetSiblingIdx(idx);
            tabs[name] = w;
        }

        public void SetSelectedTab(string name) {
            foreach (var tab in tabs) {
                tab.Value.SetSelected(tab.Key == name);
            }
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Twitch, show);
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        public void Load() {
            Cleanup();
            lifetime = Lifetime.Create("Twitch");
            OnShow?.Invoke(lifetime);
        }

        public void Rebuild() {
            if (!gameObject.activeSelf) { return; }
            Load();
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        private void Close() => Toggle(false);
        public bool TryCloseWidget() { Close(); return true; }

        public bool TryFocusSearch() {
            return wSearch.Focus();
        }

    }
}
