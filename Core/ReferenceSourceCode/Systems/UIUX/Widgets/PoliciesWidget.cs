using System;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class PoliciesWidget : BaseWidget, ICloseableWidget, IWithSearch {
        [SerializeField] private WText wHeader;
        [SerializeField] private WButton wClose;
        [SerializeField] private WGroup wListContainer;
        [SerializeField] private WGroup wContentContainer;
        [SerializeField] private ScrollRect wListScroll;
        [SerializeField] private ScrollRect wContentScroll;
        [SerializeField] private WGroup wListFooter;
        [SerializeField] private WGroup wContentFooter;
        [SerializeField] private WButtonText wListFooterButton;
        [SerializeField] private WTabRow wPolicyTabs;
        [SerializeField] private WDropdown wListDropdown;
        [SerializeField] private WInput wSearchBar;

        public WGroup ListContainer => wListContainer;
        public WGroup ContentContainer => wContentContainer;
        public ScrollRect ListScroll => wListScroll;
        public ScrollRect ContentScroll => wContentScroll;
        public WGroup ListFooter => wListFooter;
        public WGroup ContentFooter => wContentFooter;
        public WButtonText ListFooterButton => wListFooterButton;
        public WTabRow PolicyTabs => wPolicyTabs;
        public WDropdown ListDropdown => wListDropdown;
        public WInput SearchBar => wSearchBar;

        public Action<Lifetime> OnShow;

        private Lifetime showLifetime;

        public void Initialize(GameState s, Action<string> onSearch) {
            wHeader.SetText(T.Policies);
            Register(UIWidgetId.Policies, this);
            wContentFooter.DestroyChildren();
            wPolicyTabs.DestroyChildren();
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
            UIShowing.Set(UIShowingFlags.Policies , show);
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
            showLifetime = Lifetime.Create("PoliciesWidget");
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
