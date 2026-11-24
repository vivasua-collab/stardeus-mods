using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.UI;
using KL.Utils;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class QuickSearchWidget : BaseWidget {
        [SerializeField] private WButtonText wHeader;
        [SerializeField] private WInput wSearch;
        [SerializeField] private WTabRow wHotbar;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private WGroup wContainer;
        [SerializeField] private CanvasGroup cg;

        private string query;
        private string wantQuery;
        private float queryTimeout;
        private Action<string> onQuery;
        private BaseSearchResult firstResult;
        private BaseSearchAdapter selectedAdapter;
        private BaseSearchAdapter previousAdapter;
        private BaseSearchAdapter[] adapters;
        private QuickSearchContext lastSearchContext;
        private Lifetime lifetime;
        private Lifetime resultsLifetime;
        private Lifetime hotbarLifetime;
        private List<BaseSearchResult> pinned;

        public void Initialize(Action<string> onQuery, List<BaseSearchResult> pinned) {
            this.onQuery = onQuery;
            this.pinned = pinned;
            wHotbar.DestroyChildren();
            wContainer.DestroyChildren();
            wTabs.DestroyChildren();
            wHeader.Load(T.Search, Toggle);
            Register(UIWidgetId.QuickSearch, this);
            Toggle(false);
            UIUtils.LoadSearch(wSearch, UpdateInput, SubmitInput);
        }

        public Lifetime ShowHotbar(bool yes) {
            if (yes && hotbarLifetime?.IsDisposed == false
                    && wHotbar.gameObject.activeSelf) {
                return hotbarLifetime;
            }
            if (hotbarLifetime != null) {
                hotbarLifetime.Dispose();
                hotbarLifetime = null;
            }
            if (yes) {
                hotbarLifetime = lifetime.CreateChild("QuickSearch_Hotbar");
                int idx = 0;
                foreach (var p in pinned) {
                    AddPinned(hotbarLifetime, idx++, p);
                }
            }
            wHotbar.SetActiveFast(yes);
            return hotbarLifetime;
        }
        public void ShowTabs(bool yes) {
            wTabs.SetActiveFast(yes);
        }

        public void ShowNoResults() {
            if (resultsLifetime != null) {
                resultsLifetime.Dispose();
            }
            resultsLifetime = lifetime.CreateChild("QuickSearch_NoResults");
            var w = UISpawn.Widget<WText>(resultsLifetime,
                UIPrefabId.WText_QuickSearchNoResults, wContainer.Container, -1);
            var notFound = query != null && Strings.ToSearchString(query).Length > 1;
            w.Load(notFound ? T.QuickSearchNoResults : T.QuickSearchEmpty);
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.QuickSearch, show);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            if (show) {
                D.Warn("Showing quick search");
                lifetime = Lifetime.Create("QuickSearchWidget");

                var ctx = BaseSearchAdapter.CurrentContext();
                if (ctx != lastSearchContext) {
                    wSearch.SetValue(null, false);
                    query = wantQuery = null;
                    queryTimeout = 0f;
                    ClearResults();
                }
                lastSearchContext = ctx;

                if (UIShowing.FullScreenWidget) {
                    if (UIShowing.Starmap) {
                        A.S.UISig.OnHoverUISpaceObject.Send(null, false);
                    }
                    wHotbar.SetActiveFast(false);
                    UIUtils.ToggleCG(cg, true);
                } else {
                    ShowHotbar(pinned.Count > 0);
                }

                selectedAdapter = null;
                previousAdapter = null;
                if (adapters != null) {
                    DisposeResults();
                    LoadResults(adapters, true);
                } else {
                    ShowNoResults();
                }
                A.S.Toolbox.Clear();
                Coroutines.OnNextFrame(() => {
                    if (UIShowing.QuickSearch) {
                        wSearch.Focus();
                    }
                });
                Rect.SetAsLastSibling();
            } else {
                wSearch.Unfocus();
            }
            base.Toggle(show);
        }

        private void Update() {
            if (query != wantQuery) {
                queryTimeout -= GameTimer.unscaledDeltaTime;
                if (queryTimeout <= 0f) {
                    query = wantQuery;
                    onQuery(query);
                }
            }
        }

        private void SubmitInput(string txt) {
            if (txt != query) {
                query = txt;
                wantQuery = txt;
                onQuery(txt);
            }
            if (VirtualMouseInput.IsCurrent && The.Platform.IsConsole()) {
                // don't submit, virtual keyboard is likely in use
            } else {
                firstResult?.OnSelect();
            }
        }

        private void UpdateInput(string txt) {
            wantQuery = txt;
            queryTimeout = 0.2f;
        }

        public void ClearResults() {
            firstResult = null;
            previousAdapter = null;
            DisposeResults();
            if (adapters != null) {
                foreach (var ad in adapters) {
                    ad.ClearResults();
                }
            }
            ShowTabs(false);

        }
        void DisposeResults() {
            if (resultsLifetime != null) {
                resultsLifetime.Dispose();
                resultsLifetime = null;
            }
        }

        public void LoadResults(BaseSearchAdapter[] adapters, bool queryChanged) {
            this.adapters = adapters;
            if (queryChanged) {
                previousAdapter = null;
                selectedAdapter = null;
            }

            if (resultsLifetime != null) {
                resultsLifetime.Dispose();
            }
            resultsLifetime = lifetime.CreateChild("QuickSearch_Results");

            int idx = 0;
            ShowTabs(false);
            var showAdapter = selectedAdapter;
            foreach (var adapter in adapters) {
                var hasResults = adapter.ResultCount > 0;
                if (hasResults && showAdapter == null) {
                    showAdapter = adapter;
                }
                if (hasResults || adapter == selectedAdapter || adapter == previousAdapter) {
                    AddTab(idx++, adapter, showAdapter == adapter);
                }
            }
            wTabs.RebuildLayout();
            // for some reason if it's not rebuild on next frame, it can show up fucked up (probably because tabs resize themselves dynamically)
            Coroutines.OnNextFrame(wTabs.RebuildLayout);
            LoadSelectedTab(showAdapter);
        }

        private void LoadSelectedTab(BaseSearchAdapter adapter) {
            int idx = 0;
            firstResult = null;

            if (adapter != null) {
                if (adapter.AggregateResults) {
                    LoadAggregatedTab(adapter, ref idx);
                } else {
                    string lastLabel = null;
                    adapter.ForEachResult((res, label) => {
                        if (label != null && lastLabel != label) {
                            var pre = lastLabel == null ? UIPrefabId.WText_Header : UIPrefabId.WText_HeaderSeparator;
                            var txt = UISpawn.Widget<WText>(resultsLifetime, pre, wContainer.Container, idx++);
                            lastLabel = label;
                            txt.Load(label);
                        }
                        firstResult ??= res;
                        res.SpawnWidget(resultsLifetime, idx++, wContainer);
                    });
                }
            }

            if (idx == 0) {
                ShowNoResults();
            }
        }

        private void LoadAggregatedTab(BaseSearchAdapter adapter, ref int idx) {
            var groups = new List<(string label, Def def, List<BaseSearchResult> items)>();
            adapter.ForEachResult((res, label) => {
                firstResult ??= res;
                var def = res.Def;
                if (def == null) {
                    groups.Add((label, null, new List<BaseSearchResult> { res }));
                } else {
                    int i = groups.FindIndex(g => g.label == label && g.def.IdH == def.IdH);
                    if (i >= 0) {
                        groups[i].items.Add(res);
                    } else {
                        groups.Add((label, def, new List<BaseSearchResult> { res }));
                    }
                }
            });

            string lastLabel = null;
            foreach (var (label, def, items) in groups) {
                if (label != null && label != lastLabel) {
                    var pre = lastLabel == null ? UIPrefabId.WText_Header : UIPrefabId.WText_HeaderSeparator;
                    UISpawn.Widget<WText>(resultsLifetime, pre, wContainer.Container, idx++).Load(label);
                    lastLabel = label;
                }
                if (items.Count == 1) {
                    items[0].SpawnWidget(resultsLifetime, idx++, wContainer);
                } else {
                    var listBtn = UISpawn.Widget<WContentListButton>(resultsLifetime,
                        UIPrefabId.WContent2ListButton, wContainer.Container, idx++);
                    listBtn.Load(def?.Preview, def?.NameT ?? "?", items.Count.ToString());
                    listBtn.LoadOnToggle(resultsLifetime, (life, _, willShow, wasLoaded) => {
                        if (!willShow || wasLoaded) { return wasLoaded; }
                        int subIdx = 0;
                        foreach (var item in items) {
                            item.SpawnWidget(life, subIdx++, listBtn);
                        }
                        return true;
                    });
                }
            }
        }

        private void AddTab(int idx, BaseSearchAdapter adapter, bool selected) {
            var tab = UISpawn.Widget<WTabButton>(resultsLifetime, UIPrefabId.WTabButton_Narrow, wTabs.Container, idx);
            tab.Load($"{adapter.ResultLabel} ({adapter.ResultCount})",
                () => OnClickTab(adapter), null);
            tab.SetSelected(selected);
            ShowTabs(true);
        }

        private void OnClickTab(BaseSearchAdapter adapter) {
            if (previousAdapter != selectedAdapter) {
                previousAdapter = selectedAdapter;
            }
            selectedAdapter = adapter;
            LoadResults(adapters, false);
        }

        public void AddPinned(Lifetime lifetime, int idx, BaseSearchResult res) {
            res.SpawnPinned(lifetime, idx, wHotbar);
        }

        public void RemovePinned(BaseSearchResult res) {
            var children = wHotbar.Container.childCount;
            for (int i = 0; i < children; i++) {
                var child = wHotbar.Container.GetChild(i);
                if (child.gameObject.activeSelf) {
                    if (child.TryGetComponent<WQuickSearchPinnedItem>(out var pItem)) {
                        if (pItem.IsFor(res)) {
                            pItem.Dispose();
                        }
                    }
                }
            }
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            adapters = null;
            selectedAdapter = null;
            previousAdapter = null;
            firstResult = null;
            lastSearchContext = QuickSearchContext.None;
        }

        public void DragPinnedItem(WQuickSearchPinnedItem item, List<BaseSearchResult> pinned, Vector2 pos) {
            // ignore y
            pos = new Vector2(pos.x, item.Rect.GetScreenCenter().y);
            var targetItem = FindPinnedItemAtPosition(pos);
            if (targetItem != null && targetItem != item) {
                SwapPinnedItems(item, targetItem, pinned);
            }
        }

        private WQuickSearchPinnedItem FindPinnedItemAtPosition(Vector2 pos) {
            var childCount = wHotbar.Container.childCount;
            for (int i = 0; i < childCount; i++) {
                var child = wHotbar.Container.GetChild(i);
                if (!child.gameObject.activeSelf) { continue; }
                if (child.TryGetComponent<WQuickSearchPinnedItem>(out var pinnedItem)) {
                    if (child.TryGetComponent<RectTransform>(out var rect)) {
                        if (rect.ContainsScreenPoint(pos, out var _)) {
                            return pinnedItem;
                        }
                    }
                }
            }
            return null;
        }

        private void SwapPinnedItems(WQuickSearchPinnedItem item1,
                WQuickSearchPinnedItem item2, List<BaseSearchResult> pinned) {
            var rect1 = item1.GetComponent<RectTransform>();
            var rect2 = item2.GetComponent<RectTransform>();
            if (rect1 == null || rect2 == null) { return; }

            var idx1 = rect1.GetSiblingIndex();
            var idx2 = rect2.GetSiblingIndex();
            if (idx1 == idx2) { return; }

            var result1 = item1.Result;
            var result2 = item2.Result;
            var oldIdx = pinned.IndexOf(result1);
            var newIdx = pinned.IndexOf(result2);

            if (oldIdx < 0 || oldIdx >= pinned.Count) { return; }
            if (newIdx < 0 || newIdx >= pinned.Count) { return; }

            if (idx1 > idx2) {
                rect1.SetSiblingIndex(idx1 - 1);
            } else if (idx1 < idx2) {
                rect1.SetSiblingIndex(idx1 + 1);
            }

            D.Warn("Swapping pinned items {0} and {1}", oldIdx, newIdx);
            pinned[oldIdx] = result2;
            pinned[newIdx] = result1;
        }
    }
}
