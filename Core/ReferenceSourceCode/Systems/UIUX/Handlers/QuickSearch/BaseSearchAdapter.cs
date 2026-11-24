using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public abstract class BaseSearchAdapter : IComparable<BaseSearchAdapter>, IResettable {
        public abstract string Id { get; }
        public abstract string ResultLabel { get; }
        public abstract int ResultOrder { get; }
        protected readonly List<BaseSearchResult> Results = new();
        public virtual int ResultCount => Results.Count;
        protected readonly HashSet<int> displayed = new();
        public virtual bool ShowInStarmap => false;
        public virtual bool ShowInResearch => false;
        public virtual bool ShowInMap => true;
        public virtual bool AggregateResults => false;

        protected static void Register(string id, BaseSearchAdapter instance) {
            Registries.SearchAdapters.Add(id, instance);
        }


        protected void QueryItems<T>(
                IEnumerable<T> orderedItems,
                string q,
                Regex wordStart,
                Func<T, int> getId,
                Func<T, string> getSearchName,
                Func<T, string> getName,
                Func<T, string> getDesc,
                Func<T, int, BaseSearchResult> createResult) {
            var idx = 0;
            Results.Clear();
            displayed.Clear();
            // direct hits
            foreach (var item in orderedItems) {
                if (getSearchName(item).StartsWith(q)) {
                    if (displayed.Add(getId(item))) {
                        Results.Add(createResult(item, idx++));
                    }
                }
            }
            if (wordStart == null) { return; }
            // lesser hits
            foreach (var item in orderedItems) {
                if (!displayed.Contains(getId(item))) {
                    if (wordStart.IsMatch(getName(item))) {
                        if (displayed.Add(getId(item))) {
                            Results.Add(createResult(item, idx++));
                        }
                    }
                }
            }
            if (getDesc != null) {
                // least accurate hits
                foreach (var item in orderedItems) {
                    if (!displayed.Contains(getId(item))) {
                        if (wordStart.IsMatch(getDesc(item))) {
                            if (displayed.Add(getId(item))) {
                                Results.Add(createResult(item, idx++));
                            }
                        }
                    }
                }
            }
        }

        public static QuickSearchContext CurrentContext() {
            if (UIShowing.Starmap) {
                return QuickSearchContext.Starmap;
            }
            if (UIShowing.Research) {
                return QuickSearchContext.Research;
            }
            return QuickSearchContext.Map;
        }

        public static void QueryAll(string input, BaseSearchAdapter[] adapters) {
            Regex ws = input.Length > 2 ? Strings.WordStartRegex(input) : null;
            var q = Strings.ToSearchString(input);
            var ctx = CurrentContext();
            for (int i = adapters.Length - 1; i >= 0; i--) {
                var adapter = adapters[i];
                if (ctx == QuickSearchContext.Starmap) {
                    if (!adapter.ShowInStarmap) {
                        continue;
                    }
                } else if (ctx == QuickSearchContext.Research) {
                    if (!adapter.ShowInResearch) {
                        continue;
                    }
                } else if (!adapter.ShowInMap) {
                    continue;
                }
                adapter.Query(input, q, ws);
            }
        }

        protected abstract void Query(string input, string q, Regex wordStart);

        public abstract BaseSearchResult LoadResult(string resultId, int order);

        public int CompareTo(BaseSearchAdapter other) {
            return ResultOrder.CompareTo(other.ResultOrder);
        }

        public void OnReset() {
            Results.Clear();
            displayed.Clear();
            ResetAdapter();
        }

        protected virtual void ResetAdapter() { }

        // string = required header, if any
        public virtual void ForEachResult(Action<BaseSearchResult, string> act) {
            foreach (var res in Results.OrderBy(r => r.Order)) {
                act(res, null);
            }
        }

        public virtual void ClearResults() {
            Results.Clear();
        }
    }
}
