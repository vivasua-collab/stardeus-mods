using System;
using System.Collections.Generic;
using System.Linq;
using Game.Data;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class QuickSearchHandler : BaseWidgetHandler<QuickSearchWidget> {
        private readonly BaseSearchAdapter[] adapters;
        private readonly List<BaseSearchResult> pinned = new();

        public QuickSearchHandler(GameState s, QuickSearchWidget widget) : base(s, widget) {
            widget.Initialize(OnQuery, pinned);
            s.UIQuery.ShowQuickSearch.SetResponder(OnShow);
            s.UIQuery.PinQuickSearchResult.SetResponder(OnPin);
            s.UIQuery.OnDragQuickSearchPinnedItem.SetResponder(OnDragPinnedItem);
            adapters = Registries.SearchAdapters.All.Values.OrderBy(a => a.ResultOrder).ToArray();
            widget.ShowHotbar(false);
            widget.ShowTabs(false);
        }

        private bool OnDragPinnedItem(WQuickSearchPinnedItem item, Vector2 pos) {
            widget.DragPinnedItem(item, pinned, pos);
            return true;
        }

        private bool OnPin(BaseSearchResult res) {
            var idx = pinned.IndexOf(res);
            if (idx == -1) {
                var life = widget.ShowHotbar(true);
                widget.AddPinned(life, 0, res);
                pinned.Insert(0, res);
            } else {
                pinned.RemoveAt(idx);
                widget.RemovePinned(res);
                if (pinned.Count == 0) {
                    widget.ShowHotbar(false);
                }
            }
            return true;
        }

        private bool OnShow(bool show) {
            if (show) {
                PruneUnavailablePinned();
            }
            widget.Toggle(show);
            return true;
        }

        private void PruneUnavailablePinned() {
            for (int i = pinned.Count - 1; i >= 0; i--) {
                if (!pinned[i].IsPinnedAvailable) {
                    pinned.RemoveAt(i);
                }
            }
        }

        private void OnQuery(string q) {
            q = q.Trim();
            D.Warn("Searching for {0}", q);
            // q = Strings.ToSearchString(q);
            widget.ClearResults();
            if (q.Length < 2) {
                widget.ShowNoResults();
                return;
            }

            BaseSearchAdapter.QueryAll(q, adapters);
            widget.LoadResults(adapters, true);
        }

        public void OnSave(UIUXSysData data) {
            PruneUnavailablePinned();
            data.Pinned = PinnedSearchResultData.Serialize(pinned);
        }

        public void OnLoad(UIUXSysData data) {
            if (data.Pinned == null) { return; }
            PinnedSearchResultData.Deserialize(data.Pinned, pinned);
        }

        public void Cleanup() {
            Arrays.Clear(adapters);
            widget.Cleanup();
        }
    }
}
