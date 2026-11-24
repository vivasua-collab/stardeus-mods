using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class DevMenuWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        public SubPanelWidget SubPanel;
        public WGroup WItems => wItems;

        private GameState S;
        private Lifetime lifetime;
        private string selectedTabId;
        private readonly Dictionary<string, WTabButton> tabs = new();
        private readonly List<DevMenuTab> tabInstances = new();

        private void Awake() {
            gameObject.SetActiveFast(false);
        }

        public void Initialize(GameState s) {
            Register(UIWidgetId.DevMenu, this);
            S = s;
            wPanelHeader.Load("Dev Menu", Close);
            wTabs.DestroyChildren();
            wItems.DestroyChildren();
            SubPanel.Initialize();
            CreateTabs(s);
        }

        private void CreateTabs(GameState s) {
            tabInstances.Add(new DevMenuTab_Overview(s));
            #if !STARDEUS_DEMO
            tabInstances.Add(new DevMenuTab_CameraTools(s));
            tabInstances.Add(new DevMenuTab_Tunables(s));
            tabInstances.Add(new DevMenuTab_Situations(s));
            tabInstances.Add(new DevMenuTab_StoryEvents(s));
            tabInstances.Add(new DevMenuTab_Drama(s));
            #endif

            tabs.Clear();
            for (int i = 0; i < tabInstances.Count; i++) {
                CreateTab(i, tabInstances[i]);
            }
        }

        private void CreateTab(int idx, DevMenuTab tab) {
            var prefab = ResourceLibrary.FindPrefab<WTabButton>(UIPrefabId.WTabButton);
            var w = PrefabPool.Spawn(prefab, wTabs.Container);
            w.Load(tab.TabName, () => ShowTab(tab.TabId));
            w.SetSiblingIdx(idx);
            tabs[tab.TabId] = w;
        }

        public override void Toggle(bool show) {
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        public void Load() {
            Cleanup();
            lifetime = Lifetime.Create("DevMenu");

            if (selectedTabId == null) {
                selectedTabId = DevMenuTab_Overview.TabIdConst;
            }

            foreach (var tab in tabs) {
                tab.Value.SetSelected(tab.Key == selectedTabId);
            }

            var activeTab = GetActiveTab();
            int gIdx = 0;
            activeTab.LoadContent(lifetime, wItems, ref gIdx, SubPanel, Load);
        }

        public void ShowTab(string tabId) {
            selectedTabId = tabId;
            Load();
        }

        private DevMenuTab GetActiveTab() {
            foreach (var t in tabInstances) {
                if (t.TabId == selectedTabId) { return t; }
            }
            selectedTabId = DevMenuTab_Overview.TabIdConst;
            tabs[selectedTabId].SetSelected(true);
            return tabInstances[0];
        }

        public void Close() {
            S.UIQuery.ToggleDevMenu.Ask(false);
        }

        private void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            SubPanel.Close();
        }

        public bool TryCloseWidget() {
            if (SubPanel.IsShowing) {
                SubPanel.Close();
                return true;
            }
            Toggle(false);
            return true;
        }
    }
}
