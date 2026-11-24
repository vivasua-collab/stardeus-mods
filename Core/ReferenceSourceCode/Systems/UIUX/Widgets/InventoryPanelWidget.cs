using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.BodyParts;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class InventoryPanelWidget : BaseWidget, IReactsToScroll, ICloseableWidget, IWithSearch {
        private WTabButton[] tabButtons;
        [SerializeField] private WButtonText wHeader;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WSpacer emptyItemPrefab;
        [SerializeField] private WTabButton tabPrefab;
        [SerializeField] private WText groupLabelPrefab;
        [SerializeField] private Scrollbar scrollbar;
        [SerializeField] private WInput wSearch;
        [SerializeField] private WDropdown wDropSort;
        [SerializeField] private WText wDropSortText;
        [SerializeField] private WButtonText wBtnProduction;
        private Lifetime lifetime;
        private string filter;
        private Dictionary<MatType, InvMatBlock> matBlocks;
        private Dictionary<InventoryItemDef, InvEquipmentBlock> equipmentBlocks;
        private Dictionary<BodyPartDef, InvBodyPartBlock> bodyPartBlocks;
        private int currentTab;
        protected override bool IsPoolable => false;

        private enum InvSortMode { Group, Alphabetic, CountsDesc, CountsAsc }

        private static InvSortMode SortMode {
            get => (InvSortMode) CachedPlayerPrefs.GetInt("inv.panel.sort.mode", (int) InvSortMode.Group);
            set => CachedPlayerPrefs.SetInt("inv.panel.sort.mode", (int) value);
        }

        private static readonly List<string> sortOptionNames = new();
        private static List<string> SortOptionNames {
            get {
                if (sortOptionNames.Count == 0) {
                    sortOptionNames.Add(T.SortGroup);
                    sortOptionNames.Add(T.SortName);
                    sortOptionNames.Add($"{T.SortCount} {GlyphMap.ArrowDown}");
                    sortOptionNames.Add($"{T.SortCount} {GlyphMap.ArrowUp}");
                }
                return sortOptionNames;
            }
        }
        public bool PassThroughZoomOnScroll => false;
        public bool IsShowing;

        public void Initialize(GameState s, string headerText) {
            gameObject.SetActive(false);
            Register(UIWidgetId.InventoryConfig, this);
            wHeader.Load(headerText, Close);

            s.Query.TryInventorySearch.SetResponder(OnTryInvSearch);

            UIUtils.LoadSearch(wSearch, FilterItems);

            // cleanup editor preview stuff
            wItems.DestroyChildren();
            wTabs.DestroyChildren();
            wBtnProduction.Load(T.NounProductionOverview, ToggleProductionOverview);
            wDropSort.Load(SortOptionNames, (int) SortMode, SetSortMode);
            wDropSortText.Load($"{T.Sort}: {SortOptionNames[(int) SortMode]}");

            tabButtons = new WTabButton[3];
            CreateTab(0, T.MenuMaterials, () => ShowMats(false));
            CreateTab(1, T.Equipment, () => ShowEquipment(false));
            CreateTab(2, T.NounBodyParts, () => ShowBodyParts(false));
        }

        private void SetSortMode(int mode) {
            SortMode = (InvSortMode) mode;
            wDropSortText.Load($"{T.Sort}: {SortOptionNames[mode]}");
            if (!IsShowing) { return; }
            var tab = currentTab;
            Cleanup();
            switch (tab) {
                case 0: ShowMats(); break;
                case 1: ShowEquipment(); break;
                case 2: ShowBodyParts(); break;
            }
        }

        private void ToggleProductionOverview() {
            A.S.UIQuery.ToggleProduction.Ask(!UIShowing.Production);
        }

        private bool OnTryInvSearch() {
            if (IsShowing) {
                wSearch.Focus();
                return true;
            }
            return false;
        }

        private void FilterItems(string filter) {
            var searchQ = Strings.ToSearchString(filter, allowNull: true);
            if (this.filter == searchQ) {
                return;
            }
            this.filter = searchQ;
            switch (currentTab) {
                case 0: {
                    ShowMats(true);
                    break;
                }
                case 1: {
                    ShowEquipment(true);
                    break;
                }
                case 2: {
                    ShowBodyParts(true);
                    break;
                }
            }
        }

        private void ShowBodyParts(bool force = false) {
            if (SelectTab(2) || force) {
                Cleanup();
                currentTab = 2;
                DoShowBodyParts();
            }
        }

        private void ShowEquipment(bool force = false) {
            if (SelectTab(1) || force) {
                Cleanup();
                currentTab = 1;
                DoShowEquipment();
            }
        }

        private void ShowMats(bool force = false) {
            if (SelectTab(0) || force) {
                Cleanup();
                currentTab = 0;
                DoShowMats();
            }
        }

        private bool SelectTab(int num) {
            if (currentTab == num) { return false; }
            for (int i = tabButtons.Length - 1; i >= 0; i--) {
                tabButtons[i].SetSelected(i == num);
            }
            return true;
        }

        private void DoShowEquipment() {
            lifetime = Lifetime.Create("InventoryPanel_Equipment");
            Dictionary<string, List<InvEquipmentBlock>> grouped;
            if (SortMode == InvSortMode.Group) {
                grouped = new Dictionary<string, List<InvEquipmentBlock>>();
                foreach (var eb in equipmentBlocks) {
                    if (filter != null && !eb.Key.Def.SearchNameT.Contains(filter)) { continue; }
                    var group = eb.Key.SlotType.Name;
                    if (!grouped.TryGetValue(group, out var list)) { list = new(); grouped[group] = list; }
                    list.Add(eb.Value);
                }
                foreach (var mb in grouped) { mb.Value.Sort(InvEquipmentBlock.Comparer); }
            } else {
                var items = new List<InvEquipmentBlock>();
                foreach (var eb in equipmentBlocks) {
                    if (filter != null && !eb.Key.Def.SearchNameT.Contains(filter)) { continue; }
                    items.Add(eb.Value);
                }
                SortFlatByMode(items, b => b.Type.Def.NameT, b => b.Count);
                grouped = SplitIntoTwoGroups(items);
            }
            RenderNodes(grouped, (i, item) => item.SpawnWidget(lifetime, wItems.Container, this, i));
        }

        private void DoShowBodyParts() {
            lifetime = Lifetime.Create("InventoryPanel_BodyParts");
            Dictionary<string, List<InvBodyPartBlock>> grouped;
            if (SortMode == InvSortMode.Group) {
                grouped = new Dictionary<string, List<InvBodyPartBlock>>();
                foreach (var eb in bodyPartBlocks) {
                    if (filter != null && !eb.Key.Def.SearchNameT.Contains(filter)) { continue; }
                    var group = eb.Key.SlotType.NameT;
                    if (!grouped.TryGetValue(group, out var list)) { list = new(); grouped[group] = list; }
                    list.Add(eb.Value);
                }
                foreach (var mb in grouped) { mb.Value.Sort(InvBodyPartBlock.Comparer); }
            } else {
                var items = new List<InvBodyPartBlock>();
                foreach (var eb in bodyPartBlocks) {
                    if (filter != null && !eb.Key.Def.SearchNameT.Contains(filter)) { continue; }
                    items.Add(eb.Value);
                }
                SortFlatByMode(items, b => b.Type.Def.NameT, b => b.Count);
                grouped = SplitIntoTwoGroups(items);
            }
            RenderNodes(grouped, (i, item) => item.SpawnWidget(lifetime, wItems.Container, this, i));
        }

        private void DoShowMats() {
            lifetime = Lifetime.Create("InventoryPanel_Mats");
            Dictionary<string, List<InvMatBlock>> grouped;
            if (SortMode == InvSortMode.Group) {
                grouped = new Dictionary<string, List<InvMatBlock>>();
                foreach (var mb in matBlocks) {
                    if (filter != null && !mb.Key.SearchNameT.Contains(filter)) { continue; }
                    var group = mb.Key.GroupNameT;
                    if (!grouped.TryGetValue(group, out var list)) { list = new(); grouped[group] = list; }
                    list.Add(mb.Value);
                }
                foreach (var mb in grouped) { mb.Value.Sort(InvMatBlock.Comparer); }
            } else {
                var items = new List<InvMatBlock>();
                foreach (var mb in matBlocks) {
                    if (filter != null && !mb.Key.SearchNameT.Contains(filter)) { continue; }
                    items.Add(mb.Value);
                }
                SortFlatByMode(items, b => b.Type.NameT, b => b.Count);
                grouped = SplitIntoTwoGroups(items);
            }
            RenderNodes(grouped, (i, item) => item.SpawnWidget(lifetime, wItems.Container, this, i));
        }

        private void RenderNodes<T>(Dictionary<string, List<T>> grouped, Action<int, T> spawnItem) {
            var builder = new TwoColumnListBuilder<T>();
            var nodes = builder.Build(grouped);
            for (int i = 0; i < nodes.Count; i++) {
                var node = nodes[i];
                switch (node.Type) {
                    case TwoColumnNodeKind.Item: spawnItem(i, node.Item); break;
                    case TwoColumnNodeKind.Label:
                        if (!string.IsNullOrWhiteSpace(node.Label)) { AddLabel(i, node.Label); }
                        break;
                    case TwoColumnNodeKind.Placeholder: AddSpacer(i); break;
                }
            }
        }

        private void SortFlatByMode<T>(List<T> items, Func<T, string> getName, Func<T, int> getCount) {
            switch (SortMode) {
                case InvSortMode.Alphabetic:
                    items.Sort((a, b) => string.Compare(getName(a), getName(b), StringComparison.Ordinal));
                    break;
                case InvSortMode.CountsDesc:
                    items.Sort((a, b) => {
                        int cmp = getCount(b).CompareTo(getCount(a));
                        return cmp != 0 ? cmp : string.Compare(getName(a), getName(b), StringComparison.Ordinal);
                    });
                    break;
                case InvSortMode.CountsAsc:
                    items.Sort((a, b) => {
                        int cmp = getCount(a).CompareTo(getCount(b));
                        return cmp != 0 ? cmp : string.Compare(getName(a), getName(b), StringComparison.Ordinal);
                    });
                    break;
            }
        }

        private static Dictionary<string, List<T>> SplitIntoTwoGroups<T>(List<T> items) {
            int half = (items.Count + 1) / 2;
            return new Dictionary<string, List<T>> {
                { "", items.GetRange(0, half) },
                { " ", items.GetRange(half, items.Count - half) }
            };
        }

        private void AddSpacer(int idx) {
            UISpawn.Widget(lifetime, emptyItemPrefab, wItems.Container, idx);
        }
        private void AddLabel(int idx, string name) {
            var label = UISpawn.Widget(lifetime, groupLabelPrefab, wItems.Container, idx);
            label.Load(name);
        }

        private void CreateTab(int idx, string name, Action onClick) {
            var tab = UISpawn.WidgetPermanent(tabPrefab, wTabs.Container);
            tab.Load(name, onClick);
            tab.SetSiblingIdx(idx);
            tabButtons[idx] = tab;
        }

        private void Close() {
            if (IsShowing) {
                UISounds.PlayDigitalConfirm();
                IsShowing = false;
                gameObject.SetActive(false);
                if (lifetime != null) {
                    lifetime.Dispose();
                    lifetime = null;
                }
            }
        }

        private void Open() {
            if (!IsShowing) {
                IsShowing = true;
                gameObject.SetActive(true);
                Rect.SetAsLastSibling();
            }
            switch (currentTab) {
                case -1:
                case 0: {
                    currentTab = -1;
                    ShowMats();
                    break;
                }
                case 1: {
                    currentTab = -1;
                    ShowEquipment();
                    break;
                }
                case 2: {
                    currentTab = -1;
                    ShowBodyParts();
                    break;
                }
            }
        }

        public override void Toggle() {
            if (IsShowing) {
                IsShowing = false;
                gameObject.SetActive(false);
                Cleanup();
            } else {
                Open();
            }
        }

        private void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            currentTab = -1;
        }

        public bool Scroll(float delta) {
            return false;
        }

        public void RebuildMats() {
            if (IsShowing && currentTab == 0) {
                Cleanup();
                ShowMats();
            }
        }
        public void RebuildEquipment() {
            if (IsShowing && currentTab == 1) {
                Cleanup();
                ShowEquipment();
            }
        }

        public void RebuildBodyParts() {
            if (IsShowing && currentTab == 2) {
                Cleanup();
                ShowBodyParts();
            }
        }

        public void LoadMats(Dictionary<MatType, InvMatBlock> blocks) {
            matBlocks = blocks;
        }

        public void LoadEquipment(Dictionary<InventoryItemDef, InvEquipmentBlock> blocks) {
            equipmentBlocks = blocks;
        }
        public void LoadBodyParts(Dictionary<BodyPartDef, InvBodyPartBlock> blocks) {
            bodyPartBlocks = blocks;
        }

        public bool TryCloseWidget() {
            Close();
            return true;
        }

        public bool TryFocusSearch() {
            return wSearch.Focus();
        }
    }
}
