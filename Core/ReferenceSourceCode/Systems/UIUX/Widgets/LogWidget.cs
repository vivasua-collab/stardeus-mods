using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class LogWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText header;
        [SerializeField] private WTabRow tabRow;
        [SerializeField] private WInput search;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private WGroup content;
        [SerializeField] private WGroup footer;

        public WTabRow TabRow => tabRow;
        public WInput Search => search;
        public ScrollRect ScrollRect => scrollRect;
        public WGroup Content => content;
        public WGroup Footer => footer;
        public string SearchQuery => searchQuery;

        public Action<Lifetime> OnShow;

        private Lifetime lifetime;
        private GameState S;
        private string searchQuery;

        public void Initialize(GameState s, Action<string> onSearch, Action<string> onSearchSubmit) {
            S = s;
            header.Load(T.OverlayTipEventlog, Close);
            UIUtils.LoadSearch(search, onSearch, onSearchSubmit, T.SearchPlaceholder);
            content.DestroyChildren();
            tabRow.DestroyChildren();
            footer.DestroyChildren();
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Log, this);
        }

        public void SetSearchQuery(string query) {
            searchQuery = Strings.ToSearchString(query, allowNull: true);
        }

        public void ResetSearch() {
            searchQuery = null;
            search.SetValue(null, false);
        }

        public override void Toggle(bool show) {
            base.Toggle(show);
            UIShowing.Set(UIShowingFlags.EventLog, show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        private void Load() {
            Cleanup();
            lifetime = Lifetime.Create("LogWidget");
            OnShow?.Invoke(lifetime);
        }

        public void Rebuild() {
            if (!gameObject.activeSelf) { return; }
            Load();
        }

        private void Close() {
            S.UIQuery.ToggleLog.Ask(false);
        }

        public bool TryCloseWidget() {
            Toggle(false);
            return true;
        }

        public void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }
    }
}
