using System;
using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.BodyParts;
using Game.Systems.Crafting;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ProductionWidget : BaseWidget, IWithSearch, ICloseableWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WInput wSearch;
        [SerializeField] private WTabRow wTabs;

        private Lifetime lifetime;
        private GameState S;
        private string filter;
        private string wantFilter;
        private float filterTimeout;
        private bool isLoading;
        private int currentTab = -1;
        private WTabButton[] tabButtons;
        private readonly HashSet<string> collapsedCraftableIds = new();
        private readonly Dictionary<string, RectTransform> craftableRows = new();

        public WGroup WItems => wItems;

        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Production, this);
            S = s;
            wPanelHeader.Load(T.NounProductionOverview, Close);
            wItems.DestroyChildren();
            UIUtils.LoadSearch(wSearch, FilterItems);
            tabButtons = new WTabButton[4];
            CreateTab(ProductionOverviewTabs.Materials, T.MenuMaterials,
                () => ShowTab(ProductionOverviewTabs.Materials));
            CreateTab(ProductionOverviewTabs.Equipment, T.Equipment,
                () => ShowTab(ProductionOverviewTabs.Equipment));
            CreateTab(ProductionOverviewTabs.BodyParts, T.NounBodyParts,
                () => ShowTab(ProductionOverviewTabs.BodyParts));
            CreateTab(ProductionOverviewTabs.Beings, T.Beings,
                () => ShowTab(ProductionOverviewTabs.Beings));
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Production, show);
            base.Toggle(show);
            if (show) {
                if (currentTab < 0) { currentTab = 0; }
                SelectTab(currentTab);
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        private void ShowTab(int tab) {
            if (currentTab == tab) { return; }
            currentTab = tab;
            SelectTab(tab);
            Load();
        }

        private void SelectTab(int tab) {
            for (int i = tabButtons.Length - 1; i >= 0; i--) {
                tabButtons[i].SetSelected(i == tab);
            }
        }

        private void CreateTab(int idx, string name, Action onClick) {
            var tabPrefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var tab = UISpawn.WidgetPermanent(tabPrefab, wTabs.Container);
            tab.Load(name, onClick);
            tab.SetSiblingIdx(idx);
            tabButtons[idx] = tab;
        }

        public void Load() {
            Cleanup();
            craftableRows.Clear();
            lifetime = Lifetime.Create("Production");
            Coroutines.Start(LoadCraftables(), this, "LoadCraftables", false, false);
        }

        private void FilterItems(string value) {
            wantFilter = Strings.ToSearchString(value, allowNull: true);
            filterTimeout = 0.2f;
        }

        private void Update() {
            if (wantFilter == filter) { return; }
            filterTimeout -= GameTimer.unscaledDeltaTime;
            if (filterTimeout <= 0f && !isLoading) {
                filter = wantFilter;
                Load();
            }
        }

        private bool MatchesCurrentTab(Craftable cr) {
            var def = cr.ProductDef;
            if (def == null) { return false; }
            return ProductionOverviewTabs.Lists(currentTab, def);
        }

        private IEnumerator LoadCraftables() {
            isLoading = true;
            var allOrders = S.Query.GetAllCraftingOrders.Ask();

            var all = Craftable.AllOrdered;
            int idx = 0;

            for (int i = all.Length - 1; i >= 0; i--) {
                var cr = all[i];
                if (!MatchesCurrentTab(cr)) { continue; }
                if (filter != null && !cr.ProductDef.SearchNameT.Contains(filter)) { continue; }
                if (allOrders.TryGetValue(cr, out var orders)) {
                    LoadCraftable(cr, orders, ref idx);
                    if (idx % 25 == 0) {
                        yield return null;
                        if (lifetime == null) {
                            isLoading = false;
                            yield break;
                        }
                    }
                }
            }
            if (idx == 0) {
                var noResults = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WText_QuickSearchNoResults, wItems.Container, 0);
                noResults.Load(filter == null ? T.NotProducingThisCategory
                    : T.QuickSearchNoResults);
            }
            isLoading = false;
        }

        private void LoadCraftable(Craftable cr, List<CraftingOrder> orders, ref int idx) {
            if (cr.Ingredients == null) {
                D.Err("Craftable has no ingredients! {0}", cr.Id);
                return;
            }
            var siblingIdx = idx++;
            var rowLife = lifetime.CreateChild("ProdRow:" + cr.Id);
            var row = UISpawn.Widget<WProductionRow>(rowLife, UIPrefabId.WProductionRow, wItems.Container, siblingIdx);
            craftableRows[cr.Id] = row.Rect;
            var expanded = !collapsedCraftableIds.Contains(cr.Id);
            row.Load(rowLife, S, cr, orders, expanded, isExpanded => {
                if (isExpanded) collapsedCraftableIds.Remove(cr.Id);
                else collapsedCraftableIds.Add(cr.Id);
            });
            var changedCb = S.UIQuery.GetCraftableChangedCallback.Ask(cr);
            changedCb.Bind(rowLife, () => {
                var freshOrders = S.Query.GetAllCraftingOrders.Ask();
                var reIdx = row.transform.GetSiblingIndex();
                rowLife.Dispose();
                if (freshOrders.TryGetValue(cr, out var newOrders)) {
                    LoadCraftable(cr, newOrders, ref reIdx);
                }
            }, invokeImmediately: false);
        }

        public void ScrollToCraftable(Craftable cr) {
            if (!craftableRows.TryGetValue(cr.Id, out var rowRect) || rowRect == null) { return; }
            Canvas.ForceUpdateCanvases();
            var contentHeight = wItems.Container.rect.height;
            var viewportHeight = scroll.viewport.rect.height;
            var scrollableHeight = contentHeight - viewportHeight;
            if (scrollableHeight <= 0f) { return; }
            var rowTop = -rowRect.anchoredPosition.y - rowRect.sizeDelta.y / 2f - 20f;
            scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - rowTop / scrollableHeight);
        }

        private void Close() {
            Cleanup();
            S.UIQuery.ToggleProduction.Ask(false);
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            craftableRows.Clear();
        }

        public bool TryCloseWidget() {
            Close();
            return true;
        }

        public bool TryFocusSearch() {
            return wSearch.Focus();
        }

    }
}
