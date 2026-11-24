using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class LogHandler : BaseWidgetHandler<LogWidget> {
        private enum LogTabType { Stories, All, Beings, Devices, Objects, Other }

        private const int PageSize = 100;

        private readonly List<LogEntry> entryBuffer = new();
        private readonly List<WTabButton> tabButtons = new();
        private readonly LogTab_Stories storiesTab;
        private LogTabType activeTab = LogTabType.Stories;
        private string query;
        private string wantQuery;
        private float queryTimeout;
        private Lifetime showLifetime;
        private Lifetime contentLifetime;
        private RectTransform contentBlockContainer;

        public LogHandler(GameState s, LogWidget widget) : base(s, widget) {
            storiesTab = new LogTab_Stories(s, widget);
            widget.Initialize(s, OnSearch, OnSearchSubmit);
            s.UIQuery.ToggleLog.SetResponder(ToggleLog);
            s.UIQuery.RebuildLog.SetResponder(RebuildLog);
            widget.OnShow = OnShow;
            S.Sig.LogEntryAdded.AddListener(OnLogEntryAdded);
        }

        private bool ToggleLog(bool show) {
            if (show) { ClearSearch(); }
            widget.Toggle(show);
            return true;
        }

        private bool RebuildLog() {
            widget.Rebuild();
            return true;
        }

        private void OnSearch(string q) {
            wantQuery = q;
            queryTimeout = 0.2f;
        }

        private void OnSearchSubmit(string q) {
            if (q != query) {
                query = q;
                wantQuery = q;
                ApplySearchQuery(q);
            }
        }

        public void Update() {
            if (query != wantQuery) {
                queryTimeout -= GameTimer.unscaledDeltaTime;
                if (queryTimeout <= 0f) {
                    query = wantQuery;
                    ApplySearchQuery(query);
                }
            }
        }

        private void ApplySearchQuery(string q) {
            widget.SetSearchQuery(q);
            if (widget.gameObject.activeSelf) {
                RefreshContent();
            }
        }

        private void ClearSearch() {
            query = null;
            wantQuery = null;
            queryTimeout = 0f;
            widget.ResetSearch();
        }

        private void OnShow(Lifetime lifetime) {
            showLifetime = lifetime;
            contentLifetime = null;
            contentBlockContainer = null;
            tabButtons.Clear();
            storiesTab.Deactivate();
            LoadTabButtons(lifetime);
            LoadFooter(lifetime);
            RefreshContent();
        }

        private void LoadTabButtons(Lifetime lifetime) {
            var tabRow = widget.TabRow;
            int idx = 0;
            foreach (LogTabType tabType in Enum.GetValues(typeof(LogTabType))) {
                var capturedType = tabType;
                var btn = UISpawn.Widget<WTabButton>(lifetime,
                    UIPrefabId.WTabButton, tabRow.Container, idx++);
                btn.Load(GetTabName(tabType), () => {
                    if (activeTab == capturedType) { return; }
                    activeTab = capturedType;
                    UpdateTabButtonSelections();
                    RefreshContent();
                }, null);
                btn.SetSelected(activeTab == tabType);
                tabButtons.Add(btn);
            }
            tabRow.Show();
        }

        private void UpdateTabButtonSelections() {
            int i = 0;
            foreach (LogTabType tabType in Enum.GetValues(typeof(LogTabType))) {
                if (i < tabButtons.Count) {
                    tabButtons[i].SetSelected(activeTab == tabType);
                }
                i++;
            }
        }

        private void RefreshContent() {
            if (showLifetime == null) { return; }
            contentLifetime?.Dispose();
            contentLifetime = showLifetime.CreateChild("LogContent");
            contentBlockContainer = null;

            widget.Footer.gameObject.SetActiveFast(activeTab != LogTabType.Stories);

            if (activeTab == LogTabType.Stories) {
                storiesTab.Activate(RefreshContent);
                storiesTab.Refresh(contentLifetime);
                RebuildScrollLayout(true);
                return;
            }

            storiesTab.Deactivate();

            S.Query.GetAllLogEntries.Ask(entryBuffer);
            entryBuffer.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            var block = UISpawn.Block(contentLifetime, widget.Content, 0);
            contentBlockContainer = block.Container;

            var searchQuery = widget.SearchQuery;
            var filtered = new List<LogEntry>();
            for (int i = entryBuffer.Count - 1; i >= 0; i--) {
                var entry = entryBuffer[i];
                if (!IsInActiveTab(entry)) { continue; }
                if (!MatchesSearch(entry.Text, searchQuery)) { continue; }
                filtered.Add(entry);
            }

            int loadCount = Math.Min(PageSize, filtered.Count);
            for (int i = 0; i < loadCount; i++) {
                var textWidget = UISpawn.Widget<WText>(contentLifetime,
                    UIPrefabId.WContent2TextBlock, contentBlockContainer, i);
                textWidget.Load(filtered[i].Text);
            }

            if (filtered.Count > PageSize) {
                var btn = UISpawn.Widget<WButtonText>(contentLifetime,
                    UIPrefabId.WContent2ButtonSimple, contentBlockContainer, loadCount);
                LoadMoreAction(btn, filtered, loadCount);
            }
            RebuildScrollLayout(true);
        }

        private void LoadMoreAction(WButtonText btn, List<LogEntry> entries, int loadedCount) {
            int remaining = entries.Count - loadedCount;
            btn.Load(Texts.Italic($"+{remaining}"), () => {
                int sibIdx = btn.transform.GetSiblingIndex();
                int batchCount = Math.Min(PageSize, entries.Count - loadedCount);
                for (int i = 0; i < batchCount; i++) {
                    var textWidget = UISpawn.Widget<WText>(contentLifetime,
                        UIPrefabId.WContent2TextBlock, contentBlockContainer, sibIdx + i);
                    textWidget.Load(entries[loadedCount + i].Text);
                }
                int newLoadedCount = loadedCount + batchCount;
                if (newLoadedCount < entries.Count) {
                    btn.transform.SetSiblingIndex(sibIdx + batchCount);
                    LoadMoreAction(btn, entries, newLoadedCount);
                } else {
                    btn.gameObject.SetActiveFast(false);
                }
                RebuildScrollLayout(false);
            });
        }

        private void LoadFooter(Lifetime lifetime) {
            var clearBtn = UISpawn.Widget<WButtonText>(lifetime,
                UIPrefabId.WPanelButton, widget.Footer.Container, 0);
            clearBtn.Load(T.ClearHistory, () => {
                ClearActiveTab();
                RefreshContent();
            });
        }

        private void OnLogEntryAdded(LogEntry entry) {
            if (!widget.gameObject.activeSelf
                    || contentBlockContainer == null
                    || contentLifetime == null) { return; }
            if (activeTab == LogTabType.Stories) { return; }
            if (!IsInActiveTab(entry)) { return; }
            if (!MatchesSearch(entry.Text, widget.SearchQuery)) { return; }

            var textWidget = UISpawn.Widget<WText>(contentLifetime,
                UIPrefabId.WContent2TextBlock, contentBlockContainer, 0);
            textWidget.Load(entry.Text);
            RebuildScrollLayout(false);
        }

        private void RebuildScrollLayout(bool scrollToTop) {
            UIUtils.RebuildScroll(widget.ScrollRect, scrollToTop);
        }

        private void ClearActiveTab() {
            switch (activeTab) {
                case LogTabType.All:     S.Sys.Log.ClearAll();                break;
                case LogTabType.Beings:  S.Sys.Log.ClearBeingEntries();       break;
                case LogTabType.Devices: S.Sys.Log.ClearTileEntries();        break;
                case LogTabType.Objects: S.Sys.Log.ClearObjEntries();         break;
                case LogTabType.Other:   S.Sys.Log.ClearContextFreeEntries(); break;
            }
        }

        private bool IsInActiveTab(LogEntry entry) {
            return activeTab switch {
                LogTabType.All     => true,
                LogTabType.Beings  => entry.EntityType == LogEntry.TypeBeing,
                LogTabType.Devices => entry.EntityType == LogEntry.TypeTile,
                LogTabType.Objects => entry.EntityType == LogEntry.TypeObj,
                LogTabType.Other   => entry.EntityType == LogEntry.TypeContextFree,
                _ => true,
            };
        }

        private static bool MatchesSearch(string text, string searchQuery) {
            if (searchQuery == null) { return true; }
            var stripped = Strings.ToSearchString(Texts.StripTags(text), allowNull: true);
            return stripped != null && stripped.Contains(searchQuery);
        }

        private static string GetTabName(LogTabType tabType) {
            return tabType switch {
                LogTabType.Stories => T.SituationStoryevents,
                LogTabType.All     => T.All,
                LogTabType.Beings  => T.Beings,
                LogTabType.Devices => T.MenuDevices,
                LogTabType.Objects => T.MenuObjects,
                LogTabType.Other   => T.GroupOther,
                _ => tabType.ToString(),
            };
        }
    }
}
