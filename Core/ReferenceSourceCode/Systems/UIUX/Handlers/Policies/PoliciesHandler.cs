using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Policies;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class PoliciesHandler : BaseWidgetHandler<PoliciesWidget> {
        private const int MaxListResults = 40;

        private readonly BeingsSys beings;
        private readonly PoliciesSys policies;

        private Lifetime showLifetime;
        private Lifetime listLifetime;
        private Lifetime tabLifetime;

        private int filterIdx;
        private bool scrollToTop;
        // Set when the next tab refresh shows different content (another scope, another
        // tab), where the top is the right place to be. A refresh caused by an edit or
        // by something happening in the colony keeps the position instead.
        private bool scrollTabToTop;
        private string searchQuery;

        private PolicyScope scope = PolicyScope.Global;
        private BeingData selectedBeing;
        private Species selectedSpecies;

        private BasePolicyTab activeTab;
        private readonly List<BasePolicyTab> tabs = new();
        private readonly Dictionary<BasePolicyTab, WTabButton> tabButtons = new();

        private readonly List<BeingData> listedBeings = new();
        private readonly List<Species> speciesOrder = new();
        private readonly Dictionary<Species, List<BeingData>> beingsBySpecies = new();

        // Set while this handler is the one mutating a policy, so the change signal
        // doesn't rebuild the very widgets the player is interacting with.
        private bool isApplyingEdit;

        private static readonly List<string> FilterOptions = new() {
            T.All, T.NounColonist, T.NounWorker
        };

        public PoliciesHandler(GameState s, PoliciesWidget widget) : base(s, widget) {
            beings = s.Sys.Get<BeingsSys>(BeingsSys.SysId);
            policies = s.Sys.Get<PoliciesSys>(PoliciesSys.SysId);
            widget.Initialize(s, OnSearch);
            widget.OnShow = OnShow;
            widget.ListFooterButton.Load(T.AdjDefault, SelectGlobal);
            s.UIQuery.TogglePolicies.SetResponder(OnToggle);
            s.UIQuery.ShowPoliciesFor.SetResponder(OnShowFor);
            s.Sig.BeingDied.AddListener(OnBeingListChanged);
            s.Sig.BeingInitialized.AddListener(OnBeingListChanged);
            s.Sig.BeingUninitialized.AddListener(OnBeingListChanged);
            s.Sig.EquipmentPolicyChanged.AddListener(OnPolicyChanged);
        }

        private bool OnToggle(bool show) {
            widget.Toggle(show);
            if (!show) {
                // Apply what a slider drag left pending before the panel goes away.
                policies.EnforceComplianceAll();
            }
            return true;
        }

        // Opens the panel on one being's Equipment tab, for the shortcut in the entity
        // panel's Equipment section.
        private bool OnShowFor(BeingData b) {
            if (b == null) { return false; }
            if (!PassesFilter(b)) {
                // The list filter the player left behind would hide the very being we
                // were asked to configure, and ValidateSelection would drop us back to
                // the default scope.
                filterIdx = 0;
                widget.ListDropdown.SetValue(0, notify: false);
            }
            activeTab = Registries.PolicyTabs.Get(EquipmentPolicyTab.TabId);
            if (UIShowing.Policies) {
                // SelectBeing refreshes both sides but leaves the tab strip alone.
                UpdateTabSelection();
                SelectBeing(b);
                return true;
            }
            // Not open yet: OnShow reads these back when the widget builds itself.
            scope = PolicyScope.Being;
            selectedBeing = b;
            selectedSpecies = b.Species;
            return OnToggle(true);
        }

        private void OnShow(Lifetime life) {
            showLifetime = life;
            listLifetime = null;
            tabLifetime = null;
            scrollToTop = true;
            scrollTabToTop = true;
            widget.ListDropdown.Load(FilterOptions, filterIdx, OnFilterChanged);
            BuildTabs();
            RefreshList();
            RefreshTab();
        }

        private void OnFilterChanged(int idx) {
            filterIdx = idx;
            RefreshList();
        }

        private void OnSearch(string query) {
            searchQuery = Strings.ToSearchString(query, allowNull: true);
            RefreshList();
        }

        private void OnBeingListChanged(BeingData b) {
            if (!UIShowing.Policies) { return; }
            if (showLifetime == null) { return; }
            RefreshList();
            RefreshTab();
        }

        private void OnPolicyChanged() {
            if (isApplyingEdit) { return; }
            if (!UIShowing.Policies) { return; }
            if (showLifetime == null) { return; }
            RefreshList();
            RefreshTab();
        }

        // --- Tabs ---

        private void BuildTabs() {
            tabs.Clear();
            tabButtons.Clear();
            foreach (var tab in Registries.PolicyTabs.All.Values) {
                tabs.Add(tab);
            }
            tabs.Sort(CompareTabs);
            int idx = 0;
            foreach (var tab in tabs) {
                var capTab = tab;
                var w = UISpawn.Widget<WTabButton>(showLifetime,
                    UIPrefabId.WTabButton_Narrow, widget.PolicyTabs.Container, idx++);
                w.Load(tab.NameT, () => SelectTab(capTab), null);
                tabButtons[tab] = w;
            }
            if (activeTab == null || !tabs.Contains(activeTab)) {
                activeTab = tabs.Count > 0 ? tabs[0] : null;
            }
            UpdateTabSelection();
            widget.PolicyTabs.RebuildLayout();
        }

        private static int CompareTabs(BasePolicyTab a, BasePolicyTab b) {
            int cmp = a.Order.CompareTo(b.Order);
            if (cmp != 0) { return cmp; }
            return string.Compare(a.NameT, b.NameT, StringComparison.Ordinal);
        }

        private void SelectTab(BasePolicyTab tab) {
            if (activeTab == tab) { return; }
            activeTab = tab;
            UpdateTabSelection();
            scrollTabToTop = true;
            RefreshTab();
        }

        private void UpdateTabSelection() {
            foreach (var kv in tabButtons) {
                kv.Value.SetSelected(kv.Key == activeTab);
            }
        }

        private void RefreshTab() {
            var scrollOffset = UIUtils.GetScrollOffset(widget.ContentScroll);
            bool toTop = scrollTabToTop;
            scrollTabToTop = false;
            tabLifetime?.Dispose();
            tabLifetime = null;
            widget.ContentFooter.SetActiveFast(false);
            if (showLifetime == null) { return; }
            if (activeTab == null) { return; }
            if (!activeTab.SupportsScope(scope)) { return; }
            tabLifetime = showLifetime.CreateChild("PoliciesTab");
            var scopeName = ScopeName();
            SpawnScopeHeader(tabLifetime, scopeName);
            var ctx = new PolicyTabContext {
                S = S,
                Sys = policies,
                Life = tabLifetime,
                Content = widget.ContentContainer,
                Footer = widget.ContentFooter,
                Scope = scope,
                Being = scope == PolicyScope.Being ? selectedBeing : null,
                Species = scope == PolicyScope.Global ? null : selectedSpecies,
                ScopeName = scopeName,
                ListedBeings = listedBeings,
                StartGroupIdx = 1,
                Rebuild = RefreshTab,
                ApplyLive = ApplyLive,
                ApplyCommit = ApplyCommit,
                ApplyAndRebuild = ApplyAndRebuild,
                RelayoutContent = RelayoutContent,
            };
            activeTab.Build(ctx);
            if (toTop) {
                UIUtils.RebuildScroll(widget.ContentScroll, true);
            } else {
                UIUtils.RebuildScrollAt(widget.ContentScroll, scrollOffset);
            }
        }

        private void RelayoutContent() {
            UIUtils.RebuildScroll(widget.ContentScroll, false);
        }

        // Names what the right side is currently configuring. Without it the tab is
        // ambiguous: the same controls look identical for a colony, a species and one
        // colonist.
        private void SpawnScopeHeader(Lifetime life, string scopeName) {
            var block = UISpawn.Block(life, widget.ContentContainer, 0);
            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header,
                block.Container, 0);
            header.Load(scopeName);
        }

        private string ScopeName() {
            return scope switch {
                PolicyScope.Being =>
                    selectedBeing?.Mind.Name.FullNameOrNameless ?? T.AdjDefault,
                PolicyScope.Species => $"{T.Species} ({selectedSpecies?.Name})",
                _ => T.AdjDefault,
            };
        }

        // --- Edit plumbing ---

        // Continuous edit (slider drag): let the AI see the new value without
        // stripping gear or rebuilding the widget under the player's cursor.
        // The compliance sweep runs when the panel closes and on the hourly tick.
        private void ApplyLive() {
            isApplyingEdit = true;
            policies.MarkChanged();
            isApplyingEdit = false;
        }

        // Discrete edit that keeps the tab layout: apply and enforce right away.
        // Nothing on screen is refreshed here - the tab knows what its edit touched,
        // and the left list only changes when a scope gains a policy of its own.
        private void ApplyCommit() {
            isApplyingEdit = true;
            policies.CommitChange();
            isApplyingEdit = false;
        }

        // Discrete edit that changes what the tab shows. The rebuild is deferred a
        // frame because these come from dropdown callbacks, and WDropdown touches its
        // own widget again after invoking us - tearing it down mid-callback is not
        // safe.
        private void ApplyAndRebuild() {
            ApplyCommit();
            // These edits switch a scope's origin outright, which is exactly what the
            // list marks, so the list goes with them.
            RefreshList();
            Coroutines.OnNextFrame(() => {
                if (!UIShowing.Policies) { return; }
                if (showLifetime == null) { return; }
                RefreshTab();
            });
        }

        // --- Scope selection ---

        private void SelectGlobal() {
            scope = PolicyScope.Global;
            selectedBeing = null;
            selectedSpecies = null;
            scrollTabToTop = true;
            RefreshList();
            RefreshTab();
        }

        private void SelectSpecies(Species species) {
            scope = PolicyScope.Species;
            selectedSpecies = species;
            selectedBeing = null;
            scrollTabToTop = true;
            RefreshList();
            RefreshTab();
        }

        private void SelectBeing(BeingData being) {
            scope = PolicyScope.Being;
            selectedBeing = being;
            selectedSpecies = being.Species;
            scrollTabToTop = true;
            RefreshList();
            RefreshTab();
            S.UIQuery.ShowBeingPanel.Ask(being);
        }

        // --- Left list ---

        private void RefreshList() {
            if (showLifetime == null) { return; }
            listLifetime?.Dispose();
            listLifetime = showLifetime.CreateChild("PoliciesList");
            CollectBeings();
            ValidateSelection();
            SpawnList(listLifetime);
            UpdateFooterButton();
            UIUtils.RebuildScroll(widget.ListScroll, scrollToTop);
            scrollToTop = false;
        }

        private void CollectBeings() {
            listedBeings.Clear();
            speciesOrder.Clear();
            foreach (var list in beingsBySpecies.Values) {
                list.Clear();
            }
            foreach (var b in beings.All.Values) {
                if (!IsEligible(b)) { continue; }
                listedBeings.Add(b);
                if (!beingsBySpecies.TryGetValue(b.Species, out var list)) {
                    list = new List<BeingData>();
                    beingsBySpecies[b.Species] = list;
                }
                if (list.Count == 0) {
                    speciesOrder.Add(b.Species);
                }
                list.Add(b);
            }
            speciesOrder.Sort(CompareSpecies);
            foreach (var sp in speciesOrder) {
                beingsBySpecies[sp].Sort(CompareBeing);
            }
        }

        // Only beings the player controls and that can wear something at all.
        // Animals and pets declare no InventorySlotTypes, so they drop out here.
        private bool IsEligible(BeingData b) {
            if (!b.IsPlayer || b.IsGuest) { return false; }
            if (b.Inventory == null || b.Inventory.Equipment.Count == 0) { return false; }
            if (b.HasStateFlag(BeingState.Dead)) { return false; }
            if (b.Body.HasFlag(BodyFlags.Missing)) { return false; }
            return PassesFilter(b);
        }

        private bool PassesFilter(BeingData b) {
            return filterIdx switch {
                1 => b.Species.IsBiological && !b.Species.IsAnimal && !b.Species.IsPet,
                2 => !b.Species.IsBiological,
                _ => true,
            };
        }

        private bool PassesSearchFilter(BeingData b) {
            if (searchQuery == null) { return true; }
            if (b == selectedBeing) { return true; }
            return Strings.ToSearchString(b.Mind.Name.FullNameOrNameless, allowNull: false)
                .Contains(searchQuery);
        }

        private void ValidateSelection() {
            if (scope == PolicyScope.Being) {
                if (selectedBeing == null || !listedBeings.Contains(selectedBeing)) {
                    // The being died, left, or was filtered out of the list.
                    SetScopeToGlobal();
                }
                return;
            }
            if (scope == PolicyScope.Species && selectedSpecies == null) {
                SetScopeToGlobal();
            }
        }

        private void SetScopeToGlobal() {
            scope = PolicyScope.Global;
            selectedBeing = null;
            selectedSpecies = null;
            // The tab is about to show a different scope, so the old scroll position
            // no longer points at anything the player was looking at.
            scrollTabToTop = true;
        }

        private void SpawnList(Lifetime life) {
            int blockIdx = 0;
            int budget = MaxListResults;
            foreach (var sp in speciesOrder) {
                SpawnSpeciesGroup(life, ref blockIdx, sp, beingsBySpecies[sp], ref budget);
            }
        }

        private void SpawnSpeciesGroup(Lifetime life, ref int blockIdx, Species species,
                List<BeingData> group, ref int budget) {
            var filtered = new List<BeingData>();
            foreach (var b in group) {
                if (PassesSearchFilter(b)) { filtered.Add(b); }
            }
            if (filtered.Count == 0) { return; }

            var block = UISpawn.Block(life, widget.ListContainer, blockIdx++,
                UIPrefabId.WContentBlock_SidePadded);
            int idx = 0;
            SpawnSpeciesHeader(life, block.Container, species, ref idx);

            if (budget <= 0) {
                var more = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock,
                    block.Container, idx++);
                more.Load($"+{filtered.Count}. {T.UseSearchHint}");
                return;
            }

            int toShow = Math.Min(filtered.Count, budget);
            for (int i = 0; i < toShow; i++) {
                SpawnBeingRow(life, block.Container, filtered[i], ref idx);
            }
            budget -= toShow;
            if (filtered.Count > toShow) {
                var more = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock,
                    block.Container, idx++);
                more.Load($"+{filtered.Count - toShow}. {T.UseSearchHint}");
            }
        }

        // The icon-button header variant: a cog instead of a worded button, so the
        // species name never gets squeezed out by the button label. This prefab has
        // no second text, so a species that owns a policy is marked on the name.
        private void SpawnSpeciesHeader(Lifetime life, RectTransform container,
                Species species, ref int idx) {
            var w = UISpawn.Widget<WButtonIconText>(life, UIPrefabId.WContent2HeaderButtonIcon,
                container, idx++);
            bool isSelected = scope == PolicyScope.Species && selectedSpecies == species;
            // var name = isSelected ? Texts.Accented(species.Name) : species.Name;
            var name = species.Name;
            bool hasOwnPolicy = policies.SpeciesEquipment(species) != null;
            if (hasOwnPolicy) {
                name = $"{GlyphMap.SpritePoliceBadge} {name}";
            }
            var capSpecies = species;
            w.Load(IconId.CCog, name, () => SelectSpecies(capSpecies));
            UIUtils.AddTip(life, w, hasOwnPolicy
                ? $"{T.PolicyConfigureSpecies}\n{T.PolicyHasOwn}"
                : T.PolicyConfigureSpecies);
        }

        private void SpawnBeingRow(Lifetime life, RectTransform container,
                BeingData b, ref int idx) {
            var w = UISpawn.Widget<WBeingListItem2>(life, UIPrefabId.WBeingListItem2,
                container, idx++);
            var capB = b;
            w.Item.Load(b, () => SelectBeing(capB), decorateName: false);

            if (w.StatusIconsLife != null) {
                w.StatusIconsLife.Dispose();
                w.StatusIconsLife = null;
            }
            bool hasOwnPolicy = b.Mind?.EquipmentPolicy != null;
            if (hasOwnPolicy) {
                w.StatusIconsLife = life.CreateChild("PolicyIcons");
                var icon = UISpawn.Widget<WImage>(w.StatusIconsLife,
                    UIPrefabId.WStatusIcon_NoRaycast, w.StatusIcons.Container, 0);
                icon.Load(IconId.CPoliceBadge);
                UIUtils.AddTip(life, w, T.PolicyOriginIndividual);
            }
            w.StatusIcons.SetActiveFast(hasOwnPolicy);

            bool isSelected = scope == PolicyScope.Being && b == selectedBeing;
            w.ActiveBar.SetActiveFast(isSelected);
            w.SelectedArrow.SetActiveFast(isSelected);
        }

        private void UpdateFooterButton() {
            // Deliberately not accented when active: this button already sits on a
            // highlighted footer, and accent-on-accent is unreadable. The content
            // header on the right says which scope is selected.
            widget.ListFooterButton.SetText(T.PolicyDefault);
            widget.ListFooter.SetActiveFast(true);
        }

        private static int CompareSpecies(Species a, Species b) {
            int cmp = b.IsBiological.CompareTo(a.IsBiological);
            if (cmp != 0) { return cmp; }
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        private static int CompareBeing(BeingData a, BeingData b) {
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }
    }
}
