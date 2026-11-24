using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Atlas;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class EntityPanelWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButton wPanelHeader;
        [SerializeField] private WEntityHeader wEntityHeader;
        // A sibling of the header, not a child: hovering a child would also show the header's
        // codex tooltip
        [SerializeField] private WText wTaskLine;
        [SerializeField] private WTabRow wTabRow;
        [SerializeField] private GameObject wBlockWarning;
        [SerializeField] private WGroup wWarningContent;
        [SerializeField] private WGroup wControlsTop;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WGroup wControlsBottom;
        [SerializeField] private SubPanelWidget subPanel;
        [SerializeField] private WIconText warningOverlay;
        [SerializeField] private UIHeightAdjuster heightAdjuster;
        protected override bool IsPoolable => false;
        private Lifetime lifetime;
        private EntityPanelTaskLine taskLine;
        public bool IsShowing;
        [NonSerialized] public BeingData CurrentBeing;
        [NonSerialized] public ObjData CurrentObj;
        [NonSerialized] public Tile CurrentTile;
        private bool isShowingCodexTip;
        // Not derived from CurrentBeing.Being, the being can respawn while the panel is open
        private bool isDetachedBeing;
        private bool blockActions;
        private readonly List<BaseEntityTab> currentTabs = new();
        public IReadOnlyList<BaseEntityTab> CurrentTabs => currentTabs;
        private readonly List<BaseEntityWarning> currentWarnings = new();
        private BaseEntityTab lastSelectedTab;
        private BaseEntityTab currentTab;
        public BaseEntityTab CurrentTab => currentTab;
        private string pendingTabId;
        private bool hasWarningOverlay;
        private bool warningOverlayShowing;
        private (string icon, string text) storedOverlayData;
        private ScrollRect itemsScroll;
        // Lets tests tell a redundant rebuild from none
        public int RebuildCount { get; private set; }
        // Warnings and tabs read the shown entity's task only while loading, and tasks come
        // and go without whoever changed them refreshing the panel
        private AIGoal shownGoal;
        // Only a task of an IEntityPanelGoalProvider changes what the panel shows, so a change
        // between other tasks is taken over without a reload. Reloading for those closed the
        // sub-panel every time a working crafter swapped its haul task (STA-1668).
        public bool CheckOutdatedGoal() {
            if (!IsShowing) { return false; }
            var entity = ShownEntity();
            if (entity?.IsActive != true) { return false; }
            var goal = entity.Goal;
            if (goal == shownGoal) { return false; }
            if (IsShownInPanel(goal) || IsShownInPanel(shownGoal)) { return true; }
            shownGoal = goal;
            return false;
        }

        private static bool IsShownInPanel(AIGoal goal) {
            return goal?.Target is IEntityPanelGoalProvider;
        }
        public void Initialize() {
            this.SetActiveFast(false);
            Register(UIWidgetId.EntityPanel, this);
            MiscUtils.DestroyChildren(wWarningContent.Container);
            // can't destroy it since it will have some panel borders
            // MiscUtils.DestroyChildren(wControlsTop.Container);
            MiscUtils.DestroyChildren(wControlsBottom.Container);
            MiscUtils.DestroyChildren(wItems.Container);
            MiscUtils.DestroyChildren(wTabRow.Container);
            warningOverlay.SetActiveFast(false);
            itemsScroll = wItems.Rect.GetComponentInParent<ScrollRect>(includeInactive: true);
            wPanelHeader.SetOnClick(() => {
                Close();
                A.S.Query.DeselectAny.Ask();
            });
            wEntityHeader.HoverCallback.SetCallback(OnHoverHeader, OnHoverHeaderError);
            taskLine = new EntityPanelTaskLine(wTaskLine);
            taskLine.Hide();
            subPanel.Initialize();
        }

        private void OnHoverHeaderError(string obj) {
            D.Err("Error while hovering header: {0}", obj);
        }

        private Entity ShownEntity() {
            if (CurrentTile != null) {
                return EntityUtils.ParentObj(CurrentTile);
            }
            if (CurrentObj != null) {
                return CurrentObj.Obj;
            }
            return CurrentBeing?.Being;
        }

        public bool ReloadPanel() {
            if (!IsShowing) { return false; }
            if (CurrentBeing != null) {
                var b = CurrentBeing;
                Cleanup();
                CreateLifetime();
                LoadBeing(A.S, b);
                return true;
            } else if (CurrentObj != null) {
                var o = CurrentObj;
                Cleanup();
                CreateLifetime();
                LoadObj(A.S, o);
                return true;
            } else if (CurrentTile != null) {
                var t = CurrentTile;
                Cleanup();
                CreateLifetime();
                LoadTile(A.S, t);
                return true;
            }
            return false;
        }

        private void OnHoverHeader(bool isHovering) {
            if (isHovering) {
                if (CurrentTile != null) {
                    isShowingCodexTip = true;
                    A.S.UIQuery.ShowCodexTooltipFor.Ask(lifetime,
                        CodexTooltip.CreateFor(wEntityHeader, CurrentTile.Definition, optional: false, showDelay: 0.1f));
                } else if (CurrentBeing != null) {
                    isShowingCodexTip = true;
                    A.S.UIQuery.ShowCodexTooltipFor.Ask(lifetime,
                        CodexTooltip.CreateFor(wEntityHeader, CurrentBeing.Species.Def, optional: false, showDelay: 0.1f));
                } else if (CurrentObj != null) {
                    isShowingCodexTip = true;
                    A.S.UIQuery.ShowCodexTooltipFor.Ask(lifetime,
                        CodexTooltip.CreateFor(wEntityHeader, CurrentObj.Def, optional: false, showDelay: 0.1f));
                }
            } else {
                HideCodexTip();
            }
        }

        private void HideCodexTip() {
            if (isShowingCodexTip) {
                isShowingCodexTip = false;
                A.S.UIQuery.HideCodexTooltip.Ask();
            }
        }

        public bool ShowBeing(GameState s, BeingData being) {
            if (IsShowing) {
                if (CurrentBeing == null) { lastSelectedTab = null; }
                Cleanup();
            }
            CreateLifetime();
            LoadBeing(s, being);
            Show();
            s.UISig.ShowingBeingPanel.Send(being);
            return true;
        }

        private void CreateLifetime() {
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("EntityPanel");
        }

        public bool ShowObj(GameState s, ObjData obj) {
            if (IsShowing) {
                // if (obj == CurrentObj) {
                //     Close();
                //     return false;
                // }
                if (CurrentObj == null) { lastSelectedTab = null; }
                Cleanup();
            }
            CreateLifetime();
            LoadObj(s, obj);
            Show();
            return true;
        }

        public bool ShowTile(GameState s, Tile tile) {
            if (IsShowing) {
                if (CurrentTile == null) { lastSelectedTab = null; }
                // if (tile == CurrentTile) {
                //     Close();
                //     return false;
                // }
                Cleanup();
            }
            CreateLifetime();
            LoadTile(s, tile);
            Show();
            return true;
        }

        private void Show() {
            if (IsShowing) { return; }
            UIShowing.Set(UIShowingFlags.EntityPanel);
            IsShowing = true;
            Rect.SetAsLastSibling();
            this.SetActiveFast(true);
        }

        private void Cleanup() {
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            foreach (var tab in currentTabs) {
                tab.Unload();
            }
            foreach (var warning in currentWarnings) {
                warning.Unload();
            }
            HideCodexTip();
            taskLine.Hide();
            blockActions = false;
            hasWarningOverlay = false;
            warningOverlayShowing = false;
            currentWarnings.Clear();
            currentTabs.Clear();
            currentTab = null;
            wItems.SetActiveFast(false);
            wControlsTop.SetActiveFast(false);
            wControlsBottom.SetActiveFast(false);
            wBlockWarning.SetActiveFast(false);
            wEntityHeader.SetTint(Consts.ColorWhite);
            CurrentTile = null;
            CurrentObj = null;
            CurrentBeing = null;
            shownGoal = null;
            if (isDetachedBeing) {
                isDetachedBeing = false;
                A.S.UISig.DetachedBeingPanel.Send(null);
            }
        }


        private void OnClickBeing() {
            if (CurrentBeing?.Being != null) {
                EntityUtils.CameraFocusOn(CurrentBeing?.Being);
            }
        }
        private void OnClickObj() {
            if (CurrentObj?.Obj != null) {
                EntityUtils.CameraFocusOn(CurrentObj?.Obj);
            }
        }
        private void OnClickTile() {
            if (CurrentTile != null) {
                EntityUtils.CameraFocusOn(CurrentTile);
            }
        }
        private void LoadBeing(GameState s, BeingData being) {
            RebuildCount++;
            subPanel.Close(clearMemory: false);
            CurrentBeing = being;
            shownGoal = ShownEntity()?.Goal;
            being.Avatar.Load(wEntityHeader.Image);
            wEntityHeader.SetText(being.Mind.Name.FullNameOrNameless);
            wEntityHeader.SetText2(being.UI.GetBio(false, true));
            wEntityHeader.SetOnClick(OnClickBeing);
            wEntityHeader.AutoResize();
            wEntityHeader.SetFactionIcon(being.EffectiveFaction);
            taskLine.PlaceBelow(wEntityHeader.Text2);
            taskLine.Load(lifetime, s, being);
            foreach (var warning in Registries.EntityWarnings.All.Values) {
                if (warning.ShowFor(s, being, out var blockActs)) {
                    if (blockActs) {
                        blockActions = true;
                    }
                    currentWarnings.Add(warning);
                }
            }
            var unrevealed = StasisUtils.IsUnrevealed(being);
            foreach (var tab in Registries.EntityTabs.All.Values) {
                if (unrevealed && !tab.ShowForUnrevealed) { continue; }
                if (tab.ShowFor(s, being)) {
                    currentTabs.Add(tab);
                }
            }

            LoadWarnings();
            LoadTabs();
            // s.Clock.ExecuteAfterFramesForUI(1, () =>
            //     UIUtils.RebuildLayoutRecursive(wTabRow.Container));
            if (being.Being == null) {
                isDetachedBeing = true;
                s.UISig.DetachedBeingPanel.Send(being);
            }
        }

        private void LoadTabs() {
            currentTabs.Sort(BaseEntityTab.CompareTabs);
            int idx = 0;
            BaseEntityTab rememberedTab = null;
            BaseEntityTab requestedTab = null;
            var requestedTabId = pendingTabId;
            pendingTabId = null;
            foreach (var tab in currentTabs) {
                var tabButton = tab.SpawnTab(lifetime, wTabRow.Container, idx++);
                tabButton.SetOnClick(() => {
                    lastSelectedTab = null;
                    ShowTab(tab);
                });
                if (requestedTabId == tab.Id) {
                    requestedTab = tab;
                } else if (lastSelectedTab == tab) {
                    rememberedTab = tab;
                }
            }
            var showTab = requestedTab ?? rememberedTab;
            if (showTab == null) {
                if (currentTabs.Count > 0) {
                    ShowTab(currentTabs[0]);
                }
            } else {
                ShowTab(showTab);
            }
            wTabRow.SetActiveFast(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(wTabRow.Container);
        }

        private void LoadWarnings() {
            if (currentWarnings.Count > 0) {
                bool needsOverlay = false;
                (string icon, string text) overlayData = (null, null);
                currentWarnings.Sort(BaseEntityWarning.CompareWarnings);
                int idx = 0;
                foreach (var warning in currentWarnings) {
                    warning.LoadContent(lifetime, wWarningContent, ref idx);
                    if (!needsOverlay && warning.AddWarningOverlay) {
                        needsOverlay = true;
                        overlayData = warning.WarningOverlayData;
                    }
                }
                wBlockWarning.SetActiveFast(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(wWarningContent.Container);
                if (needsOverlay) {
                    hasWarningOverlay = true;
                    storedOverlayData = overlayData;
                    Coroutines.OnNextFrame(() => {
                        if (currentTab?.SuppressWarningOverlay == true) { return; }
                        ShowWarningOverlay();
                    });
                } else {
                    warningOverlay.SetActiveFast(false);
                }
            } else {
                wBlockWarning.SetActiveFast(false);
                warningOverlay.SetActiveFast(false);
            }
        }

        private void ShowWarningOverlay() {
            if (!hasWarningOverlay) { return; }
            warningOverlay.Load(storedOverlayData.icon, storedOverlayData.text);
            var off = wTabRow.Rect.anchoredPosition.y - wTabRow.Rect.sizeDelta.y;
            var ofMax = warningOverlay.Rect.offsetMax;
            ofMax.y = off;
            warningOverlay.Rect.offsetMax = ofMax;
            warningOverlay.SetActiveFast(true);
            warningOverlayShowing = true;
        }

        private void ShowTab(BaseEntityTab tab) {
            var tabChanged = currentTab != null && currentTab != tab;
            subPanel.Close(clearMemory: false);
            currentTab?.Deactivate();
            lastSelectedTab?.Deactivate();
            int tabIdx = 0;
            foreach (var t in currentTabs) {
                if (t == tab && tabIdx > 0) {
                    lastSelectedTab = tab;
                }
                t.SpawnedButton.SetSelected(tab == t);
                tabIdx++;
            }
            currentTab = tab;
            if (tab.SuppressWarningOverlay) {
                warningOverlay.SetActiveFast(false);
                warningOverlayShowing = false;
            } else if (hasWarningOverlay && !warningOverlayShowing) {
                Coroutines.OnNextFrame(ShowWarningOverlay);
            }
            tab.CreateLifetime(lifetime);
            tab.BlockActions = blockActions;
            var section = "top controls";
            bool footerLoaded;
            try {
                wControlsTop.SetActiveFast(tab.LoadTopControls(wControlsTop));
                section = "content";
                wItems.SetActiveFast(tab.LoadContent(wItems));
                section = "footer";
                footerLoaded = tab.LoadFooter(wControlsBottom);
            } catch (Exception ex) {
                object ntt = CurrentTile;
                ntt ??= CurrentBeing;
                ntt ??= CurrentObj;
                D.LogEx(ex, "Failed loading tab {0} {1} for entity {2}", section, tab, ntt);
                tab.Deactivate();
                wControlsTop.SetActiveFast(false);
                wItems.SetActiveFast(false);
                wControlsBottom.SetActiveFast(false);
                Coroutines.OnNextFrame(Close);
                return;
            }
            wControlsBottom.SetActiveFast(footerLoaded);
            if (tabChanged) {
                UIUtils.RebuildScroll(itemsScroll, scrollToTop: true);
            }
            Coroutines.OnFrame(2, wTabRow.RebuildLayout);
            if (footerLoaded) {
                Coroutines.OnFrame(2, heightAdjuster.Reset);
            }
            Coroutines.OnNextFrame(UIHoverFix.RefreshHover);
        }

        private void LoadObj(GameState s, ObjData obj) {
            RebuildCount++;
            subPanel.Close(clearMemory: false);
            CurrentObj = obj;
            shownGoal = ShownEntity()?.Goal;
            string header;
            if (obj.Def.MatType != null) {
                header = T.NounMaterial;
            } else if (obj.BodyPart != null) {
                header = T.NounBodyPart;
            } else if (InventoryItemDef.HasForDef(obj.Def.IdH)) {
                header = T.Equipment;
            } else {
                header = T.NounObject;
            }
            wEntityHeader.SetSprite(null);
            wEntityHeader.SetImage(obj.Def.Preview);
            wEntityHeader.SetTint(obj.Color);
            wEntityHeader.SetText(obj.Def.NameT);
            if (obj.Obj?.Pile != null) {
                obj.Obj.Pile.StackSizeB.Bind(lifetime, ss => {
                    if (ss > 1) {
                        wEntityHeader.SetText($"{obj.Def.NameT} x{ss}");
                    } else {
                        wEntityHeader.SetText(obj.Def.NameT);
                    }
                });
            }
            wEntityHeader.SetText2(obj.Def.DescT);
            wEntityHeader.SetFactionIcon(null);
            wEntityHeader.AutoResize();
            wEntityHeader.SetOnClick(OnClickObj);
            foreach (var warning in Registries.EntityWarnings.All.Values) {
                if (warning.ShowFor(s, obj, out var blockActs)) {
                    if (blockActs) {
                        blockActions = true;
                    }
                    currentWarnings.Add(warning);
                }
            }
            foreach (var tab in Registries.EntityTabs.All.Values) {
                if (tab.ShowFor(s, obj)) {
                    currentTabs.Add(tab);
                }
            }

            LoadWarnings();
            LoadTabs();
        }

        private void LoadTile(GameState s, Tile tile) {
            RebuildCount++;
            subPanel.Close(clearMemory: false);
            CurrentTile = tile;
            shownGoal = ShownEntity()?.Goal;
            wEntityHeader.SetSprite(null);
            wEntityHeader.SetSprite(tile.Definition.PreviewSprite);
            wEntityHeader.SetTint(Consts.ColorWhite);
            wEntityHeader.SetText(tile.NameOrLabel);
            wEntityHeader.SetText2(tile.Definition.DescT);
            wEntityHeader.SetFactionIcon(null);
            wEntityHeader.AutoResize();
            wEntityHeader.SetOnClick(OnClickTile);

            foreach (var warning in Registries.EntityWarnings.All.Values) {
                if (warning.ShowFor(s, tile, out var blockActs)) {
                    if (blockActs) {
                        blockActions = true;
                    }
                    currentWarnings.Add(warning);
                }
            }
            foreach (var tab in Registries.EntityTabs.All.Values) {
                if (tab.ShowFor(s, tile)) {
                    currentTabs.Add(tab);
                }
            }

            LoadWarnings();
            LoadTabs();
        }

        public void Close() {
            if (!IsShowing) { return; }
            D.Log("Closing Entity Panel");
            UIShowing.Unset(UIShowingFlags.EntityPanel);
            subPanel.Close(clearMemory: false);
            lastSelectedTab = null;
            currentTab = null;
            Cleanup();
            IsShowing = false;
            this.SetActiveFast(false);
        }

        public void BeingRenamed(BeingData being) {
            if (CurrentBeing == being) {
                wEntityHeader.SetText(being.Mind.Name.FullNameOrNameless);
                wEntityHeader.AutoResize();
            }
        }

        public SubPanelWidget GetSubPanel() {
            return subPanel;
        }

        public bool CloseSubPanel() {
            if (subPanel.IsShowing) {
                subPanel.Close();
                return true;
            }
            return false;
        }

        public bool ReloadTab(BaseEntityTab tab) {
            if (tab == currentTab) {
                ShowTab(tab);
                return true;
            }
            return false;
        }

        public bool ShowSelectedTab(string tabId) {
            if (string.IsNullOrEmpty(tabId)) {
                return false;
            }
            pendingTabId = tabId;
            foreach (var tab in currentTabs) {
                if (tab.Id != tabId) { continue; }
                pendingTabId = null;
                ShowTab(tab);
                return true;
            }
            return true;
        }

        public bool TryCloseWidget() {
            if (subPanel.IsShowing) {
                subPanel.Close();
                return true;
            }
            Close();
            A.S.Query.DeselectAny.Ask();
            return true;
        }

        public bool ResetSelectedTab() {
            lastSelectedTab = null;
            if (currentTab != null) {
                currentTab = null;
                if (UIShowing.EntityPanel) {
                    ReloadPanel();
                }
                return true;
            }
            return false;
        }
    }
}
