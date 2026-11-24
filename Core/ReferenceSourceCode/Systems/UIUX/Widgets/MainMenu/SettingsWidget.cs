using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class SettingsWidget : BaseWidget {
        [SerializeField] private WButton btnClose;
        [SerializeField] private WGroup tabsContainer;
        [SerializeField] private WGroup container;
        [SerializeField] private WGroup footer;
        [SerializeField] private ScrollRect scroll;
        private readonly Dictionary<string, BaseSettingsPage> pages = new();
        private BaseSettingsPage activePage;
        private BaseSettingsPage defaultPage;
        private Lifetime lifetime;
        private Vector2 originPosition;
        public Action OnClose;
        private void Awake() {
            tabsContainer.DestroyChildren();
            container.DestroyChildren();
            footer.DestroyChildren();
            btnClose.Load(Close);
            originPosition = Rect.anchoredPosition;
            CreateTabs();
            Close();
        }

        private void Update() {
            activePage?.Update();
        }

        private void AddPage(BaseSettingsPage page, int idx) {
            var btn = UISpawn.WidgetPermanent<WTabButton>(
                UIPrefabId.WTabButtonVertical, tabsContainer.Rect);
            btn.PrepareLayout();
            btn.SetSiblingIdx(idx);
            btn.Load(page.TabName, () => SetPage(page));
            page.SetButton(btn);
            pages.Add(page.Id, page);
        }

        private void CreateTabs() {
            int idx = 0;
            defaultPage = null;
            foreach (var page in Registries.SettingsPages.All.Values.OrderBy(p => p.Order)) {
                // D.Warn("Creating setting tab: {0}", page);
                if (!page.IsHidden) {
                    defaultPage ??= page;
                    AddPage(page, idx++);
                }
            }
        }

        // Close unloads the page but keeps it to reopen on: without this, the next Show and
        // ShowPage unloaded it a second time, saving binding overrides and the settings file
        // twice and running BeforeUnloadTab on a page already unloaded
        private bool isActivePageLoaded;

        private void UnloadActivePage() {
            if (isActivePageLoaded) {
                activePage?.Unload();
                isActivePageLoaded = false;
            }
        }

        private void SetPage(BaseSettingsPage page) {
            // D.Warn("Showing settings page: {0}", page);
            UnloadActivePage();
            activePage = page;
            scroll.verticalNormalizedPosition = 1;

            page.ShowTab(lifetime, container, footer);
            isActivePageLoaded = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        }

        public override void Toggle(bool show) {
            if (show) {
                Show();
            } else {
                Close();
            }
        }

        public void Show() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            lifetime = Lifetime.Create("SettingsWidget");
            Rect.anchoredPosition = originPosition;
            if (activePage == null) {
                SetPage(defaultPage);
            } else {
                SetPage(activePage);
            }
            gameObject.SetActiveFast(true);
        }

        public void Close() {
            UnloadActivePage();
            gameObject.SetActiveFast(false);
            OnClose?.Invoke();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        public void ShowPage(string id) {
            if (!gameObject.activeSelf) {
                Show();
            }
            if (activePage?.Id != id) {
                if (pages.TryGetValue(id, out var page)) {
                    SetPage(page);
                } else {
                    D.Err("Settings Page not found by id: {0}", id);
                }
            }
        }
    }
}
