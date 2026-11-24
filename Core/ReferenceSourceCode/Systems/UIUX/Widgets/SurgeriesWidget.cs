using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class SurgeriesWidget : BaseWidget, ICloseableWidget, IWithSearch {
        [SerializeField] private WButton wClose;
        [SerializeField] private WGroup wListContainer;
        [SerializeField] private WGroup wBodyContainer;
        [SerializeField] private ScrollRect wListScroll;
        [SerializeField] private WGroup wListFooter;
        [SerializeField] private WButtonText wFooterButton;
        [SerializeField] private WTabRow wBodyTabs;
        [SerializeField] private WDropdown wListDropdown;
        [SerializeField] private WTabButton wTabButton_Head;
        [SerializeField] private WTabButton wTabButton_Body;
        [SerializeField] private WTabButton wTabButton_Organs;
        [SerializeField] private WRawImage wBeingPreview;
        [SerializeField] private WInput wSearchBar;
        [SerializeField] private WText wSelectedBeingName;

        public WGroup ListContainer => wListContainer;
        public WGroup BodyContainer => wBodyContainer;
        public ScrollRect ListScroll => wListScroll;
        public WGroup ListFooter => wListFooter;
        public WButtonText FooterButton => wFooterButton;
        public WTabRow BodyTabs => wBodyTabs;
        public WDropdown ListDropdown => wListDropdown;
        public WTabButton TabHead => wTabButton_Head;
        public WTabButton TabBody => wTabButton_Body;
        public WTabButton TabOrgans => wTabButton_Organs;
        public WRawImage BeingPreview => wBeingPreview;
        public WInput SearchBar => wSearchBar;
        public WText SelectedBeingName => wSelectedBeingName;

        public Action<Lifetime> OnShow;

        private Lifetime showLifetime;

        public void Initialize(GameState s, Action<string> onSearch) {
            Register(UIWidgetId.Surgeries, this);
            wClose.Load(Close);
            UIUtils.LoadSearch(wSearchBar, onSearch);
            Close();
        }

        public bool TryFocusSearch() {
            return wSearchBar.Focus();
        }

        private void Close() {
            Toggle(false);
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Surgeries, show);
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        private void Load() {
            Cleanup();
            showLifetime = Lifetime.Create("SurgeriesWidget");
            OnShow?.Invoke(showLifetime);
        }

        private void Cleanup() {
            showLifetime?.Dispose();
            showLifetime = null;
        }

        public bool TryCloseWidget() {
            if (gameObject.activeSelf) {
                Close();
                return true;
            }
            return false;
        }
    }
}
