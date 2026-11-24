using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class CodexHandler : BaseWidgetHandler<CodexWidget> {
        private readonly List<BaseCodexTab> tabs = new();
        private BaseCodexTab currentTab;
        private Lifetime tabsLifetime;
        private string query;
        private string wantQuery;
        private float queryTimeout;

        public CodexHandler(GameState s, CodexWidget widget) : base(s, widget) {
            widget.Initialize(OnSearch, OnSearchSubmit);
            s.UIQuery.ShowCodexEntry.SetResponder(OnShowCodexEntry);
            s.UIQuery.ToggleCodex.SetResponder(ToggleCodex);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.UIQuery.TryOpenCodexLink.SetResponder(OnTryOpenLink);
            widget.OnShow = OnShow;
            widget.OnClose = OnCodexClosed;
            widget.OnTabChanged = OnTabChanged;
            widget.OnContentChanged = OnContentChanged;
            RegisterTabs(s);
        }

        private void OnSearchSubmit(string q) {
            if (q != query) {
                query = q;
                wantQuery = q;
                ApplySearchQuery(q);
            }
        }

        private void OnSearch(string q) {
            wantQuery = q;
            queryTimeout = 0.2f;
        }

        private bool OnTryOpenLink(string link) {
            D.Warn("Trying to open codex link: {0}", link);
            if (string.IsNullOrWhiteSpace(link)) {
                return false;
            }

            string originalLink = link;
            string sectionId = link;
            string subSectionId = null;
            if (link.Contains(':')) {
                var split = link.Split(':', 2);
                sectionId = split[0];
                subSectionId = split[1];
                if (string.IsNullOrWhiteSpace(subSectionId)) {
                    subSectionId = null;
                }
            }

            if (string.Equals(sectionId, "def", StringComparison.OrdinalIgnoreCase)) {
                var def = The.Defs.TryGet(subSectionId);
                if (def == null) {
                    D.Err("Def not found by id: {0}", subSectionId);
                    return false;
                }
                if (UIShowing.Tutorial) {
                    var qc = QuickChoice.Create();
                    qc.AddButton(T.OpenInBackground, () => {
                        def.ShowManualEntry();
                    });
                    qc.Show();
                    return false;
                }
                def.ShowManualEntry();
                return true;
            }

            if (TryResolveManualLink(sectionId, subSectionId, out var concept)) {
                if (concept == null) {
                    return true;
                }
                return OnShowCodexEntry(concept);
            }

            var tab = FindTabBySectionId(sectionId);
            if (tab == null) {
                D.Err("Codex link Section not found: {0}", originalLink);
                The.SysSig.ShowTooltip.Send($"{Texts.Red(T.NotAvailable)}: {originalLink}", true);
                return false;
            }

            if (subSectionId != null && !TrySelectTabTarget(tab, subSectionId)) {
                D.Err("Codex section failed to find link: {0}->{1}", sectionId, subSectionId);
                The.SysSig.ShowTooltip.Send($"{Texts.Red(T.NotAvailable)}: {originalLink}", true);
                return false;
            }

            currentTab = tab;
            ClearSearch();
            if (!widget.gameObject.activeSelf) {
                widget.Toggle(true);
            } else {
                widget.Rebuild();
            }
            return true;
        }

        private bool TryResolveManualLink(string sectionId, string subSectionId, out CodexConceptPage concept) {
            concept = null;
            if (!string.Equals(sectionId, "Manual", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (subSectionId == null) {
                var manualTab = FindTab<CodexManualTab>();
                if (manualTab == null) {
                    return false;
                }
                currentTab = manualTab;
                ClearSearch();
                if (!widget.gameObject.activeSelf) {
                    widget.Toggle(true);
                } else {
                    widget.Rebuild();
                }
                return true;
            }

            if (!subSectionId.Contains('/')) {
                concept = CodexConceptPage.FindGroup(subSectionId);
            } else {
                concept = CodexConceptPage.FindById(subSectionId);
            }
            return concept != null;
        }

        private BaseCodexTab FindTabBySectionId(string sectionId) {
            foreach (var tab in tabs) {
                if (string.Equals(tab.Id, sectionId, StringComparison.OrdinalIgnoreCase)) {
                    return tab;
                }
            }

            if (string.Equals(sectionId, "CodexTips", StringComparison.OrdinalIgnoreCase)) {
                foreach (var tab in tabs) {
                    if (tab is CodexTipsTab) {
                        return tab;
                    }
                }
            }

            return null;
        }

        private bool TrySelectTabTarget(BaseCodexTab tab, string subSectionId) {
            if (tab is CodexFactionsTab factionsTab) {
                return factionsTab.ShowEntry(subSectionId);
            }
            if (tab is CodexShipClassesTab shipClassesTab) {
                return shipClassesTab.ShowEntry(subSectionId);
            }
            if (tab is CodexShipTagsTab shipTagsTab) {
                return shipTagsTab.ShowEntry(subSectionId);
            }
            return false;
        }

        private void OnToggleOverlay(OverlayInfo ov, bool yes) {
            if (yes && ov.IdH == OverlayIdH.Codex) {
                ToggleCodex(true);
            }
        }

        private void RegisterTabs(GameState s) {
            tabs.Add(new CodexManualTab());
            if (!s.IsAchievementsBlocked &&
                    !The.Prefs.GetBool(
                        Pref.GDisableAchievements,
                        PrefH.GDisableAchievements, false)) {
                tabs.Add(new CodexAchievementsTab());
            }
            tabs.Add(new CodexMaterialsTab());
            tabs.Add(new CodexBlueprintsTab());
            tabs.Add(new CodexTipsTab());
            tabs.Add(new CodexFactionsTab());
            tabs.Add(new CodexSpeciesTab());
            tabs.Add(new CodexShipClassesTab());
            tabs.Add(new CodexShipTagsTab());
            tabs.Sort(BaseCodexTab.CompareTabs);
            foreach (var tab in tabs) {
                tab.Init(s, widget);
            }
        }

        private bool ToggleCodex(bool yes) {
            widget.Toggle(yes);
            return true;
        }

        private bool OnShowCodexEntry(CodexConceptPage concept) {
            if (concept == null) {
                return false;
            }
            if (UIShowing.Tutorial) {
                var con = concept;
                var qc = QuickChoice.Create();
                qc.AddButton($"{T.OpenCodex}: {con.TitleT}", () => {
                    A.S.UIQuery.CloseTutorial.Ask();
                    OnShowCodexEntry(con);
                });
                qc.Show();
                return false;
            }
            var manualTab = FindTab<CodexManualTab>();
            if (manualTab != null) {
                manualTab.ShowEntry(concept);
                if (currentTab != manualTab) {
                    currentTab = manualTab;
                }
            }
            ClearSearch();
            if (!widget.gameObject.activeSelf) {
                widget.Toggle(true);
            } else {
                widget.Rebuild();
            }
            return true;
        }

        private T FindTab<T>() where T : BaseCodexTab {
            foreach (var tab in tabs) {
                if (tab is T t) {
                    return t;
                }
            }
            return null;
        }

        private void OnShow(Lifetime lifetime) {
            ClearSearch();
            currentTab ??= tabs.Count > 0 ? tabs[0] : null;
            LoadTabsAndContent(lifetime);
        }

        private void OnCodexClosed() {
            FindTab<CodexManualTab>()?.ClearHistory();
        }

        private void OnTabChanged(Lifetime lifetime) {
            LoadTabsAndContent(lifetime);
        }

        private void OnContentChanged() {
            RefreshCurrentTabContent();
        }

        private void LoadTabsAndContent(Lifetime lifetime) {
            if (tabsLifetime != null) {
                tabsLifetime.Dispose();
                tabsLifetime = null;
            }
            tabsLifetime = lifetime.CreateChild("CodexTabs");
            LoadTabButtons(tabsLifetime);
            ShowTab(currentTab, lifetime);
        }

        private void LoadTabButtons(Lifetime lifetime) {
            int idx = 0;
            foreach (var tab in tabs) {
                var capturedTab = tab;
                var btn = tab.SpawnTab(lifetime, widget.TabRow.Container, idx++);
                btn.SetOnClick(() => {
                    if (currentTab == capturedTab) {
                        return;
                    }
                    currentTab = capturedTab;
                    widget.Rebuild();
                });
            }
            widget.TabRow.Show();
            LayoutRebuilder.ForceRebuildLayoutImmediate(widget.TabRow.Container);
        }

        private void ShowTab(BaseCodexTab tab, Lifetime lifetime) {
            if (tab == null) {
                return;
            }

            foreach (var t in tabs) {
                t.SetSelected(t == tab);
                if (t != tab) {
                    t.Deactivate();
                }
            }

            tab.Activate(lifetime);
            tab.LoadIndexSection(widget.Index, widget.ScrollIndex);
            RefreshTabContent(tab);
        }

        private void RefreshCurrentTabContent() {
            if (currentTab == null || widget.CurrentLifetime == null) {
                return;
            }
            RefreshTabContent(currentTab);
        }

        private void RefreshTabContent(BaseCodexTab tab) {
            tab.LoadContentSection(widget.Content);
            if (tab.HasFooter) {
                widget.ShowFooter(true);
                tab.LoadFooterSection(widget.Footer);
            } else {
                widget.ShowFooter(false);
            }
        }

        public void OnSave(SystemsDataSpecial sd) {
            sd.Codex = CodexConceptPage.SaveState();
        }

        public void OnLoad(CodexSysData data) {
            CodexConceptPage.LoadState(data);
        }

        public void Update() {
            if (query != wantQuery) {
                queryTimeout -= GameTimer.unscaledDeltaTime;
                if (queryTimeout <= 0f) {
                    query = wantQuery;
                    ApplySearchQuery(query);
                }
            }
        }

        private void ClearSearch() {
            query = null;
            wantQuery = null;
            queryTimeout = 0f;
            widget.ResetSearch();
        }

        private void ApplySearchQuery(string q) {
            widget.SetSearchQuery(q);
            if (widget.gameObject.activeSelf) {
                widget.Rebuild();
            }
        }
    }
}
