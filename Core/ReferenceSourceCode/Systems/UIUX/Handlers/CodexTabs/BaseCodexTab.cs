using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public abstract class BaseCodexTab {
        public abstract string Id { get; }
        public abstract int Order { get; }
        public abstract string TabName { get; }
        public abstract string TabIcon { get; }

        protected GameState S;
        protected CodexWidget widget;
        protected Lifetime lifetime;
        protected Lifetime Lifetime => sectionLifetime ?? lifetime;
        protected string SearchQuery => widget?.SearchQuery;
        protected bool HasSearchQuery => SearchQuery != null;
        private WTabButton spawnedButton;
        private Lifetime contentLifetime;
        private Lifetime footerLifetime;
        private Lifetime indexLifetime;
        private Lifetime sectionLifetime;

        public WTabButton SpawnedButton => spawnedButton;

        public void Init(GameState s, CodexWidget widget) {
            S = s;
            this.widget = widget;
        }

        public WTabButton SpawnTab(Lifetime parentLife, RectTransform parent, int idx) {
            spawnedButton = UISpawn.Widget<WTabButton>(parentLife,
                UIPrefabId.WTabButton, parent, idx);
            spawnedButton.Load(TabName, null);
            return spawnedButton;
        }

        public void SetSelected(bool selected) {
            spawnedButton?.SetSelected(selected);
        }

        public void Activate(Lifetime parentLife) {
            Deactivate();
            lifetime = parentLife.CreateChild($"CodexTab_{Id}");
        }

        public void Deactivate() {
            DisposeSectionLifetimes();
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
        }

        public void LoadIndexSection(WGroup index, ScrollRect scroll) {
            if (lifetime == null) {
                return;
            }
            indexLifetime?.Dispose();
            indexLifetime = lifetime.CreateChild("Index");
            sectionLifetime = indexLifetime;
            try {
                LoadIndex(index);
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
                scroll.verticalNormalizedPosition = 1f;
            } finally {
                sectionLifetime = null;
            }
        }

        public void LoadContentSection(WGroup content) {
            if (lifetime == null) {
                return;
            }
            contentLifetime?.Dispose();
            contentLifetime = lifetime.CreateChild("Content");
            sectionLifetime = contentLifetime;
            try {
                LoadContent(content);
                widget.Scroll.verticalNormalizedPosition = 1f;
            } finally {
                sectionLifetime = null;
            }
        }

        public void LoadFooterSection(WGroup footer) {
            if (lifetime == null) {
                return;
            }
            footerLifetime?.Dispose();
            footerLifetime = lifetime.CreateChild("Footer");
            sectionLifetime = footerLifetime;
            try {
                LoadFooter(footer);
            } finally {
                sectionLifetime = null;
            }
        }

        private void DisposeSectionLifetimes() {
            sectionLifetime = null;
            indexLifetime?.Dispose();
            indexLifetime = null;
            contentLifetime?.Dispose();
            contentLifetime = null;
            footerLifetime?.Dispose();
            footerLifetime = null;
        }

        public abstract void LoadIndex(WGroup index);
        public abstract void LoadContent(WGroup content);
        public virtual void LoadFooter(WGroup footer) { }

        public virtual bool HasFooter => false;

        public void Rebuild() {
            widget?.Rebuild();
        }

        public void RebuildContent() {
            widget?.RebuildContent();
        }

        protected bool MatchesSearch(params string[] values) {
            if (!HasSearchQuery) {
                return true;
            }

            foreach (var value in values) {
                var searchValue = Strings.ToSearchString(value, allowNull: true);
                if (searchValue != null && searchValue.Contains(SearchQuery)) {
                    return true;
                }
            }

            return false;
        }

        public static int CompareTabs(BaseCodexTab a, BaseCodexTab b) {
            return a.Order.CompareTo(b.Order);
        }
    }
}
