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
    public sealed class CodexWidget : BaseWidget, ICloseableWidget, IWithSearch {
        [SerializeField] private WButtonText panelHeader;
        [SerializeField] private WTabRow tabRow;
        [SerializeField] private WInput search;
        [SerializeField] private WGroup index;
        [SerializeField] private WGroup content;
        [SerializeField] private WGroup footer;
        [SerializeField] private GameObject footerBlock;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private ScrollRect scrollIndex;
        private Lifetime lifetime;
        public Action OnClose;
        public Action<Lifetime> OnShow;
        public Action<Lifetime> OnTabChanged;
        public Action OnContentChanged;
        public WTabRow TabRow => tabRow;
        public WGroup Index => index;
        public WGroup Content => content;
        public WGroup Footer => footer;
        public ScrollRect Scroll => scroll;
        public WInput Search => search;
        public ScrollRect ScrollIndex => scrollIndex;
        public Lifetime CurrentLifetime => lifetime;
        public string SearchQuery => searchQuery;
        private Action<string> onSearch;
        private Action<string> onSearchSubmit;
        private string searchQuery;

        private void Awake() {
            UIUtils.LoadSearch(search, OnSearch, OnSearchSubmit, T.SearchPlaceholder);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            // and fix the viewport to not be degenerate
            var vp = scroll.viewport;
            vp.anchorMin = Vector2.zero;
            vp.anchorMax = Vector2.one;
            vp.offsetMin = Vector2.zero;
            vp.offsetMax = Vector2.zero;
            Close();
        }

        private void OnSearchSubmit(string q) {
            onSearchSubmit?.Invoke(q);
        }

        private void OnSearch(string q) {
            onSearch?.Invoke(q);
        }

        public void Initialize(Action<string> onSearch, Action<string> onSearchSubmit) {
            panelHeader.Load($"{T.Codex}  <size=70%><voffset=0.1em>{Texts.Grey(Texts.HTTPLink("https://stardeusgame.com/codex/", T.VerbViewOnline))}</voffset></size>", Close);
            this.onSearch = onSearch;
            this.onSearchSubmit = onSearchSubmit;
            Register(UIWidgetId.Codex, this);
        }

        public void ResetSearch() {
            searchQuery = null;
            search.SetValue(null, false);
        }

        public void SetSearchQuery(string query) {
            searchQuery = Strings.ToSearchString(query, allowNull: true);
        }

        public override void Toggle(bool show) {
            if (show) {
                Show();
            } else {
                Close();
            }
        }

        public void ShowFooter(bool show) {
            footerBlock.SetActiveFast(show);
        }

        public void Show() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            lifetime = Lifetime.Create("Codex");
            UIShowing.Set(UIShowingFlags.Codex);
            gameObject.SetActiveFast(true);
            OnShow?.Invoke(lifetime);
            Rect.SetAsLastSibling();
        }

        public void Rebuild() {
            if (!gameObject.activeSelf) {
                return;
            }
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            lifetime = Lifetime.Create("Codex");
            OnTabChanged?.Invoke(lifetime);
        }

        public void RebuildContent() {
            if (!gameObject.activeSelf) {
                return;
            }
            OnContentChanged?.Invoke();
        }

        public void Close() {
            search.Unfocus();
            UIShowing.Unset(UIShowingFlags.Codex);
            gameObject.SetActiveFast(false);
            OnClose?.Invoke();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        public bool TryCloseWidget() {
            Close();
            return true;
        }

        public bool TryFocusSearch() {
            return search.Focus();
        }
    }
}
