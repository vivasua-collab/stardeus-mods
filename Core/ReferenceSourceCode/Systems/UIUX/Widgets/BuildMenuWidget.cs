using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Input;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BuildMenuWidget : BaseWidget, IWithSearch {
        [SerializeField] private WButtonText wHeader;
        [SerializeField] private WInput wSearch;
        [SerializeField] private WTabRow wTabRow;
        [SerializeField] private WGroup content;
        [SerializeField] private ScrollRect scroll;
        private Lifetime lifetime;
        private Lifetime tabLifetime;

        private string wantedFilter;
        private string filter;
        private float filterTimeout;

        private readonly Dictionary<string, WTabButton> spawnedTabs = new();
        private readonly Dictionary<string, List<Def>> cats = new();
        private readonly Dictionary<string, (ITool tool, List<string> toolParams)> devToolData = new();
        private string currentTab;
        private ToolPlaceTiles buildTool;
        private BuildMenuPicks picks;

        public void Initialize(ToolPlaceTiles buildTool, BuildMenuPicks picks) {
            this.buildTool = buildTool;
            this.picks = picks;
            Close();
            CheckSize();
            Register(UIWidgetId.BuildMenu, this);
            wTabRow.DestroyChildren();
            content.DestroyChildren();
            wHeader.Load(null, Close);

            UIUtils.LoadSearch(wSearch, FilterItems, SubmitFilter);
        }

        private void SubmitFilter(string filter) {
            if (VirtualMouseInput.IsCurrent && The.Platform.IsConsole()) {
                // don't build on submit, virtual keyboard is likely in use
                return;
            }
            ClearFilter();
            var q = Strings.ToSearchString(filter);
            if (q.Length < 2) { return; }
            foreach (var t in cats) {
                foreach (var def in t.Value) {
                    if (def.SearchNameT.Contains(q)) {
                        StartBuilding(PickInTab(def, t.Value));
                        return;
                    }
                }
            }
            if (A.AddDevTools) {
                foreach (var kv in devToolData) {
                    var (tool, toolParams) = kv.Value;
                    foreach (var param in toolParams) {
                        if (DevToolParamMatchesSearch(tool, param, q)) {
                            A.S.Toolbox.SetActive(tool, param);
                            return;
                        }
                    }
                }
            }
        }

        public void Show() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("BuildMenu");
            if (spawnedTabs.Count == 0) {
                BuildTabs();
            }
            if (currentTab == null) {
                currentTab = "Floors";
            }
            if (LoadFiltered()) {
                return;
            }
            // 1. figure out what tabs do we need to have
            // 2. see if spawned tabs match, if not spawn or respawn tabs
            // 3. see which tab is selected, if none, select first tab that has content
            // 4. figure out what the tab content should be, and load it if it changed
            if (devToolData.ContainsKey(currentTab)) {
                LoadDevToolTab(currentTab);
            } else {
                // A tab the HUD reveal hides has items, but no button
                if (!spawnedTabs.ContainsKey(currentTab)
                        || !cats.TryGetValue(currentTab, out var tab)) {
                    currentTab = spawnedTabs.ContainsKey("Floors")
                        ? "Floors"
                        : spawnedTabs.Keys.Where(cats.ContainsKey)
                            .OrderBy(buildTool.OrderForSubmenu).First();
                    tab = cats[currentTab];
                }
                LoadTab(currentTab, tab);
            }
        }

        // The Codex's Build button: the menu opens on the def's tab, and the player picks
        // the def there
        public void ShowFor(Def def) {
            var tabId = ToolPlaceTiles.MenuTabOf(def);
            // Asked for by name, as building from it would: the tab stays from now on
            A.S.UIQuery.RevealHudKey.Ask(HudRevealRules.TabKey(tabId));
            if (!string.IsNullOrEmpty(def.BuildGroup)) {
                // The group's button shows the def that was asked for
                picks.Set(def.BuildGroup, def);
            }
            if (filter != null || wantedFilter != null) {
                ClearFilter();
            }
            currentTab = tabId;
            Toggle(true);
        }

        public void BuildTabs() {
            var wTabPrefab = ResourceLibrary.FindPrefab<WTabButton>(
                UIPrefabId.WTabButton_Narrow);
            cats.Clear();
            devToolData.Clear();

            foreach (var param in buildTool.UnlockedParams) {
                var def = The.Defs.Get(param);
                var tabId = ToolPlaceTiles.MenuTabOf(def);

                if (!cats.TryGetValue(tabId, out var items)) {
                    items = new List<Def>();
                    cats[tabId] = items;
                }
                items.Add(def);
            }

            spawnedTabs.Clear();
            var ui = A.S.UIQuery;
            List<WTabButton> newTabs = null;
            foreach (var t in cats) {
                t.Value.Sort(Def.NameComparer);
                var tabId = t.Key;
                // Hidden while the tutorial has not asked for it yet. Its items stay in cats,
                // so search still finds them, and building one reveals the tab.
                if (!ui.IsHudRevealed.Ask(HudRevealRules.TabKey(tabId))) { continue; }
                var menuKey = tabId.ToLowerInvariant();
                var nameKey = $"menu.{menuKey}";
                nameKey = Texts.RemapToolMenuName(nameKey);
                var descKey = $"{nameKey}.desc";
                var title = nameKey.TOrNull();
                var desc = descKey.TOrNull();
                if (title == null) {
                    title = T.MenuUngrouped;
                    desc = T.MenuUngroupedDesc;
                }
                var tab = PrefabPool.Spawn(wTabPrefab, wTabRow.Container);
                tab.Load(title, () => LoadTab(tabId, cats[tabId]), null);
                spawnedTabs.Add(tabId, tab);
                if (ui.TakeNewBuildTabHint.Ask(tabId)) {
                    newTabs ??= new List<WTabButton>();
                    newTabs.Add(tab);
                }
            }

            if (A.AddDevTools) {
                foreach (var tool in A.S.Toolbox.AvailableTools
                        .Where(t => t.IsDevTool && !t.SkipMenu
                            && t.IsConfigurable && t.AvailableParams?.Count > 0)
                        .OrderBy(t => t.NameT)) {
                    var tabId = $"dev_{tool.Id}";
                    devToolData[tabId] = (tool, tool.AvailableParams);
                    var tab = PrefabPool.Spawn(wTabPrefab, wTabRow.Container);
                    tab.Load(tool.NameT ?? tool.Id, () => LoadDevToolTab(tabId), null);
                    spawnedTabs.Add(tabId, tab);
                }
            }

            int idx = 0;
            foreach (var st in spawnedTabs
                    .Where(st => cats.ContainsKey(st.Key))
                    .OrderBy(st => buildTool.OrderForSubmenu(st.Key))) {
                st.Value.SetSiblingIdx(idx++);
            }
            foreach (var st in spawnedTabs
                    .Where(st => devToolData.ContainsKey(st.Key))
                    .OrderBy(st => st.Key)) {
                st.Value.SetSiblingIdx(idx++);
            }
            // because tabs resize themselves after loading
            Coroutines.OnNextFrame(() => {
                LayoutRebuilder.ForceRebuildLayoutImmediate(wTabRow.Container);
                // A tab the reveal just added points at itself once, now that it sits
                // where it will stay
                if (newTabs == null) { return; }
                foreach (var tab in newTabs) {
                    if (tab != null && tab.gameObject.activeInHierarchy) {
                        UIFocusRing.Request(tab.Rect);
                    }
                }
            });
        }

        public string CurrentTab => currentTab;

        public bool TryGetTabRect(string tabId, out RectTransform rect) {
            rect = spawnedTabs.TryGetValue(tabId, out var tab) ? tab.Rect : null;
            return rect != null;
        }

        public static string BuildMenuPathFor(Def def) {
            var parts = def.Id.Split("/");
            var tabId = parts[1];
            var menuKey = tabId.ToLowerInvariant();
            var nameKey = $"menu.{menuKey}";
            nameKey = Texts.RemapToolMenuName(nameKey);
            var descKey = $"{nameKey}.desc";
            var title = nameKey.TOrNull();
            if (title == null) {
                title = T.MenuUngrouped;
            }
            return $"{title} {GlyphMap.ArrowRight} { def.NameT}";
        }

        private bool DevToolParamMatchesSearch(ITool tool, string param, string q) {
            var def = tool.DefFor(param);
            var searchName = def != null
                ? def.SearchNameT
                : Strings.ToSearchString(tool.MenuNameFor(param).TOrNull() ?? param);
            return searchName.Contains(q);
        }

        private bool LoadFiltered() {
            if (string.IsNullOrWhiteSpace(filter)) {
                return false;
            }
            var q = Strings.ToSearchString(filter);
            if (tabLifetime != null) {
                tabLifetime.Dispose();
            }
            if (q.Length <= 1) {
                LayoutContentNow();
                return true;
            }
            var itemPrefab = ResourceLibrary.FindPrefab<WButtonIconText>(UIPrefabId.WBuildMenuButton);
            int idx = 0;
            tabLifetime = lifetime.CreateChild("BuildMenuFilter");
            var matches = new List<Def>();
            foreach (var t in cats) {
                foreach (var def in t.Value) {
                    if (def.SearchNameT.Contains(q)) {
                        matches.Add(def);
                    }
                }
            }
            SpawnEntries(itemPrefab, ref idx, matches);
            if (A.AddDevTools) {
                foreach (var kv in devToolData) {
                    var (tool, toolParams) = kv.Value;
                    foreach (var param in toolParams) {
                        if (DevToolParamMatchesSearch(tool, param, q)) {
                            SpawnDevToolButton(itemPrefab, idx++, tool, param);
                        }
                    }
                }
            }
            scroll.verticalNormalizedPosition = 1f;
            LayoutContentNow();
            return true;
        }

        private void LoadDevToolTab(string tabId) {
            if (filter != null) {
                ClearFilter();
            }
            currentTab = tabId;
            foreach (var st in spawnedTabs) {
                st.Value.SetSelected(tabId == st.Key);
            }
            if (tabLifetime != null) {
                tabLifetime.Dispose();
            }
            tabLifetime = lifetime.CreateChild("BuildMenuDevTab");
            var (tool, toolParams) = devToolData[tabId];
            var itemPrefab = ResourceLibrary.FindPrefab<WButtonIconText>(UIPrefabId.WBuildMenuButton);
            int idx = 0;
            foreach (var param in toolParams) {
                SpawnDevToolButton(itemPrefab, idx++, tool, param);
            }
            scroll.verticalNormalizedPosition = 1f;
            LayoutContentNow();
        }

        private void SpawnDevToolButton(WButtonIconText itemPrefab, int idx, ITool tool, string param) {
            var def = tool.DefFor(param);
            var name = def?.NameT ?? tool.MenuNameFor(param).TOrNull() ?? param;
            var w = UISpawn.Widget(tabLifetime, itemPrefab, content.Container, idx);
            w.Load(null, name, () => {
                A.S.Toolbox.SetActive(tool, param);
            });
            var sprite = def != null ? def.PreviewSprite : Images.Sprite(tool.MenuIconFor(param));
            w.SetSprite(sprite);
            w.SetTint(Consts.ColorWhite);
            if (def != null) {
                UIUtils.AddCodexTooltip(tabLifetime, w.gameObject, w, def, optional: true, showDelay: 0.1f, anchorTop: false);
            }
        }

        private void SpawnButton(WButtonIconText itemPrefab, int idx, Def def) {
            var w = UISpawn.Widget(tabLifetime, itemPrefab, content.Container, idx);
            w.Load(null, def.NameT, () => {
                StartBuilding(def);
            });
            w.SetSprite(def.PreviewSprite);
            w.SetTint(Consts.ColorWhite);
            UIUtils.AddCodexTooltip(tabLifetime, w.gameObject, w, def, optional: false, showDelay: 0.1f, anchorTop: false);
        }

        private void SpawnEntries(WButtonIconText itemPrefab, ref int idx, IReadOnlyList<Def> defs) {
            foreach (var entry in BuildMenuGroups.Collapse(defs)) {
                if (entry.Group == null) {
                    SpawnButton(itemPrefab, idx++, entry.Def);
                } else {
                    SpawnGroupButton(idx++, entry.Group, entry.Members);
                }
            }
        }

        private void SpawnGroupButton(int idx, string group, List<Def> members) {
            var life = tabLifetime;
            var w = UISpawn.Widget<WBuildMenuButtonExpandable>(life,
                UIPrefabId.WBuildMenuButtonExpandable, content.Container, idx);
            LoadGroupPick(life, w, picks.Resolve(group, members));
            w.Expand.SetInteractable(true);
            w.Expand.SetOnClick(() => ShowGroupChoice(life, w, group, members));
        }

        private void LoadGroupPick(Lifetime life, WBuildMenuButtonExpandable w, Def def) {
            w.Button.Load(null, def.NameT, () => {
                StartBuilding(def);
            });
            w.Button.SetSprite(def.PreviewSprite);
            w.Button.SetTint(Consts.ColorWhite);
            // replaces the previous pick's tooltip
            w.LoadCodexTooltip(life, def);
        }

        private void ShowGroupChoice(Lifetime life, WBuildMenuButtonExpandable w, string group,
                List<Def> members) {
            var qc = QuickChoice.Create();
            qc.Widget = w.Expand.Rect;
            qc.AttachAbove = true;
            foreach (var member in members) {
                var def = member;
                qc.AddIconButton(def.PreviewSprite, def.NameT, () => {
                    picks.Set(group, def);
                    LoadGroupPick(life, w, def);
                    StartBuilding(def);
                });
            }
            qc.OnUpdate = () => {
                if (life.IsDisposed || !UIShowing.BuildMenu) {
                    qc.Close();
                }
            };
            qc.Show();
        }

        // What building a searched def means when it belongs to a group: the group's pick
        private Def PickInTab(Def def, List<Def> tabDefs) {
            var group = def.BuildGroup;
            if (string.IsNullOrEmpty(group)) {
                return def;
            }
            foreach (var entry in BuildMenuGroups.Collapse(tabDefs)) {
                if (entry.Group == group) {
                    return picks.Resolve(group, entry.Members);
                }
            }
            return def;
        }

        private void StartBuilding(Def def) {
            A.S.Toolbox.SetActive(buildTool, def.Id);
        }

        private void LoadTab(string tabId, List<Def> items) {
            if (currentTab == tabId) {
                D.Warn("Loading same tab: {0}", tabId);
            }
            if (filter != null) {
                ClearFilter();
            }
            currentTab = tabId;
            foreach (var st in spawnedTabs) {
                st.Value.SetSelected(tabId == st.Key);
            }
            if (tabLifetime != null) {
                tabLifetime.Dispose();
            }
            tabLifetime = lifetime.CreateChild("BuildMenuTab");
            var itemPrefab = ResourceLibrary.FindPrefab<WButtonIconText>(
                UIPrefabId.WBuildMenuButton);
            int idx = 0;
            SpawnEntries(itemPrefab, ref idx, items);
            scroll.verticalNormalizedPosition = 1f;
            LayoutContentNow();
        }

        // UIHeightAdjuster fits the menu to its content in LateUpdate, but newly spawned items
        // are laid out only when the canvas renders, after that. Laying them out now lets it
        // fit the menu in the same frame, instead of showing one frame at the previous tab's
        // height on every tab switch.
        private void LayoutContentNow() {
            if (!gameObject.activeInHierarchy) { return; }
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        }

        private void ClearFilter() {
            filter = null;
            wantedFilter = null;
            filterTimeout = 0f;
            wSearch.SetValue(null, false);
        }

        private void FilterItems(string query) {
            wantedFilter = query;
            filterTimeout = 0.05f;
        }

        public override void Toggle(bool show) {
            if (show) {
                Show();
            } else {
                wSearch.Unfocus();
                A.S.UIQuery.HideCodexTooltip.Ask();
            }
            UIShowing.Set(UIShowingFlags.BuildMenu, show);
            base.Toggle(show);
            if (show) {
                Rect.SetAsLastSibling();
                // Show() spawned the items while the menu was still inactive
                LayoutContentNow();
            } else if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
                // lifetime will dispose it
                tabLifetime = null;
            }
        }

        private void Update() {
            if (wantedFilter != filter) {
                filterTimeout -= GameTimer.unscaledDeltaTime;
                if (filterTimeout <= 0f) {
                    filter = wantedFilter;
                    Show();
                }
            }
        }

        private void Close() {
            Toggle(false);
        }

        [ContextMenu("Check Size")]
        public void CheckSize() {
            var width = Rect.rect.width;
            var sw = Screen.width / Rect.localScale.x;
            var pad = 32f / Rect.localScale.x;
            if (width > sw - pad) {
                Rect.sizeDelta = new Vector2(sw - pad,
                    Rect.sizeDelta.y);
            }
        }

        public void Rebuild() {
            D.Log("Rebuilding build menu");
            if (spawnedTabs.Count > 0) {
                foreach (var tab in spawnedTabs.Values) {
                    PrefabPool.Despawn(tab.gameObject);
                }
                spawnedTabs.Clear();
            }
            if (gameObject.activeSelf) {
                Show();
            }
        }

        public bool TryFocusSearch() {
            return wSearch.Focus();
        }
    }
}
