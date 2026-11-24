using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class BotDockTab : BaseEntityTab {
        public const string TabId = "BotDock";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BotDockTab());
        }

        private const string SubId = "BotDock";
        private Tile tile;
        private BotDockComp dock;
        private GameState S;
        private WBase contentBtn;
        // Docking state changes far more often than the assignment (a bot walking to its dock
        // and back), and a full reload rebuilds the open bot list under the cursor (STA-1821),
        // so only an assignment change reloads the tab
        private uint loadedAssignedId;
        private WButtonIconText2 assignedBotRow;
        private WButtonIconText dockButton;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.Dock;

        public override bool ShowFor(GameState s, Tile tile) {
            S = s;
            if (!tile.IsConstructed) { return false; }
            if (tile.ENode?.IsReachable == false) { return false; }
            if (!tile.TryGetComponent<BotDockComp>(out var dc)) { return false; }
            this.tile = tile;
            this.dock = dc;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            loadedAssignedId = dock.AssignedBeingDataId;
            assignedBotRow = null;
            dock.StateChangedB.Bind(lifetime, OnDockStateChanged, false);

            var block = UISpawn.Block(lifetime, group, 0);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.AssignedBot);

            if (dock.IsAssigned) {
                LoadAssignedBot(block.Container, ref idx);
            } else {
                LoadNotAssigned(block.Container, ref idx);
            }

            LoadWorkRadius(group);

            if (SubPanelWidget.IsOpenFor == SubId && contentBtn != null) {
                // Force layout rebuild so contentBtn has correct world
                // position before the subpanel repositions against it
                LayoutRebuilder.ForceRebuildLayoutImmediate(group.Container);
                if (dock.IsAssigned) {
                    var bd = S.Query.FindBeingData.Ask(dock.AssignedBeingDataId);
                    if (bd != null) {
                        ShowAssignedBotSubPanel(contentBtn, bd);
                    }
                } else {
                    ShowAssignSubPanel(contentBtn);
                }
                var btn = contentBtn;
                Coroutines.OnNextFrame(() => {
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(btn, connectorOnly: false);
                });
            }

            return true;
        }

        private void OnDockStateChanged() {
            if (dock == null) { return; }
            if (dock.AssignedBeingDataId != loadedAssignedId) {
                ReloadTab();
                return;
            }
            assignedBotRow?.SetText2(DockStatusText());
            if (dockButton != null) {
                LoadDockButton(dockButton, dock.AssignedDockable);
            }
        }

        private string DockStatusText() {
            var dockable = dock.AssignedDockable;
            if (dockable == null) { return null; }
            if (dockable.IsDocked) { return T.Docked; }
            if (dockable.IsDockingNow || dockable.HasDockingGoal) { return T.Docking; }
            return T.NotDocked;
        }

        // Belongs to the dock, so it shows whether a bot is assigned or not
        private void LoadWorkRadius(WGroup group) {
            var block = UISpawn.Block(lifetime, group, 1);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.WorkRadius);

            var row = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, block.Container, idx++);
            row.Item.Load(IconId.CMove, T.Range, null);
            row.Slider.Load(dock.WorkRadius, v => {
                var r = Mathf.RoundToInt(v);
                if (r == dock.WorkRadius) { return; }
                dock.SetWorkRadius(r);
                // only the ring: a StateChangedB here would reload the tab mid-drag, and
                // TilesSelected would also move the camera and reload the panel
                S.Sys.Particles.SelectionVFX?.ShowSelectedTiles(S.Query.GetSelectedTiles.Ask());
            }, 0f, BotDockComp.MaxWorkRadius);
            row.Slider.SetValueTextFunc(v => {
                var r = Mathf.RoundToInt(v);
                return r == 0 ? "∞" : $"{r}";
            });
            UIUtils.AddTip(lifetime, row, T.WorkRadiusTip);
        }

        private void LoadAssignedBot(RectTransform container, ref int idx) {
            var beingData = S.Query.FindBeingData.Ask(dock.AssignedBeingDataId);
            if (beingData == null) {
                D.Err("Dock is assigned to a non-existent being id! {0} | {1}",
                    dock.AssignedBeingDataId, dock);
                dock.SetAssignedBeingData(0);
                return;
            }
            var b = beingData.Being;

            // Assigned bot - click opens reassign / unassign subpanel
            var botWidget = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, container, idx++);
            contentBtn = botWidget;
            assignedBotRow = botWidget;
            botWidget.Load(null, beingData.Name, DockStatusText(), () => {
                ShowAssignedBotSubPanel(botWidget, beingData);
            });
            beingData.Avatar.Load(botWidget.Image);
        }

        private void LoadNotAssigned(RectTransform container, ref int idx) {
            var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, container, idx++);
            contentBtn = w;
            w.Load(IconId.CCross, T.NotAssigned, Texts.Grey(T.VerbAssign), () => {
                ShowAssignSubPanel(w);
            });
        }

        private void ShowAssignedBotSubPanel(WBase forWidget, BeingData assignedBot) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.AssignedBot);
            int gIdx = 0;

            // Block 0: current assignment with unassign action
            var assignedBlock = UISpawn.Block(l, subPanel.Container, gIdx++);
            int idx = 0;

            var assignedHeader = UISpawn.Widget<WButtonText2>(l,
                UIPrefabId.WContent2HeaderButton, assignedBlock.Container, idx++);
            assignedHeader.Load(T.AssignedBot, () => {
                UnassignBot(assignedBot);
            }, T.VerbUnassign);

            var assignedBotBtn = UISpawn.Widget<WButtonIconText2>(l,
                UIPrefabId.WContent2Button, assignedBlock.Container, idx++);
            assignedBotBtn.Load(null, assignedBot.Name, null, () => {
                BeingUtils.SelectFocused(assignedBot);
            });
            assignedBot.Avatar.Load(assignedBotBtn.Image);

            // Block 1: unassigned bots
            SpawnAssignableBotsBlock(l, subPanel, gIdx++);
            // Block 2: bots assigned to other docks
            SpawnDockedBotsBlock(l, subPanel, gIdx++);
            subPanel.Show(forWidget, SubId);
        }

        private void UnassignBot(BeingData assignedBot) {
            var dockable = dock.AssignedDockable;
            if (dockable?.IsDocked == true) {
                dock.Undock(true);
            }
            S.Query.AssignBotDock.Ask(assignedBot, null);
        }

        private void ShowAssignSubPanel(WBase forWidget) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.VerbAssign);

            int gIdx = 0;
            SpawnAssignableBotsBlock(l, subPanel, gIdx++);
            SpawnDockedBotsBlock(l, subPanel, gIdx++);
            subPanel.Show(forWidget, SubId);
        }

        private void SpawnAssignableBotsBlock(Lifetime l,
                SubPanelWidget subPanel, int gIdx) {
            var block = UISpawn.Block(l, subPanel.Container, gIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.BotsWithoutDock);

            var botCnt = S.Beings.AliveCount;
            if (botCnt == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return;
            }

            var pool = ArrayPool<BeingData>.Shared;
            var botArr = pool.Rent(botCnt);
            var assignableCnt = S.Query.FindDockables.Ask(botArr, true);

            if (assignableCnt == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                int ass = 0;
                for (int i = 0; i < assignableCnt; i++) {
                    var dockable = botArr[i];
                    if (dockable == null) { continue; }
                    if (!dockable.EffectiveFaction.IsPlayer) { continue; }
                    var w = UISpawn.Widget<WContentButton2>(l,
                        UIPrefabId.WContent2Button2, block.Container, idx++);
                    w.Item.Load(null, dockable.Name, () => {
                        BeingUtils.Select(dockable, true);
                    });
                    w.Button.Load(T.VerbAssign, () => {
                        S.Query.AssignBotDock.Ask(dockable, dock);
                    });
                    dockable.Avatar.Load(w.Item.Image);
                    ass++;
                }
                if (ass == 0) {
                    var empty = UISpawn.Widget<WIconText>(l,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    empty.Load(IconId.CInfo, T.Empty);
                }
            }

            pool.Return(botArr, true);
        }

        private void SpawnDockedBotsBlock(Lifetime l,
                SubPanelWidget subPanel, int gIdx) {
            var block = UISpawn.Block(l, subPanel.Container, gIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.BotsWithDock);

            var botCnt = S.Beings.AliveCount;
            if (botCnt == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return;
            }

            var pool = ArrayPool<BeingData>.Shared;
            var botArr = pool.Rent(botCnt);
            var allCnt = S.Query.FindDockables.Ask(botArr, false);

            int added = 0;
            for (int i = 0; i < allCnt; i++) {
                var bd = botArr[i];
                if (bd == null) { continue; }
                if (!bd.EffectiveFaction.IsPlayer) { continue; }
                var dockId = bd.Dockable.DockId;
                // Skip unassigned and bots assigned to this dock
                if (dockId == 0 || dockId == dock.EntityId) { continue; }
                var w = UISpawn.Widget<WContentButton2>(l,
                    UIPrefabId.WContent2Button2, block.Container, idx++);
                w.Item.Load(null, bd.Name, () => {
                    BeingUtils.Select(bd, true);
                });
                w.Button.Load(T.VerbReassign, () => {
                    S.Query.AssignBotDock.Ask(bd, dock);
                });
                bd.Avatar.Load(w.Item.Image);
                added++;
            }
            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }

            pool.Return(botArr, true);
        }

        public override bool LoadFooter(WGroup group) {
            int idx = 0;
            idx = AddAutoAssignToggle(group, idx);
            idx = AddDockActionButton(group, idx);
            idx = AddAutoDockToggle(group, idx);
            return idx > 0;
        }

        private int AddAutoAssignToggle(WGroup group, int idx) {
            if (dock.IsAssigned) { return idx; }
            var toggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            toggle.Load(T.AutoAssign, dock.AutoAssign, isOn => {
                dock.AutoAssign = isOn;
            });
            UIUtils.AddTip(lifetime, toggle, T.AutoAssignDockToBotTip);
            return idx;
        }

        private int AddDockActionButton(WGroup group, int idx) {
            dockButton = null;
            if (!dock.IsAssigned) { return idx; }
            var beingData = S.Query.FindBeingData.Ask(dock.AssignedBeingDataId);
            if (beingData?.Being == null) { return idx; }
            var dockableComp = dock.AssignedDockable;
            if (dockableComp == null) { return idx; }

            dockButton = UISpawn.Widget<WButtonIconText>(lifetime,
                UIPrefabId.WPanelButtonIcon, group.Container, idx++);
            LoadDockButton(dockButton, dockableComp);
            return idx;
        }

        private void LoadDockButton(WButtonIconText btn, BotDockableComp dockableComp) {
            if (dockableComp == null) {
                btn.SetInteractable(false);
                return;
            }
            if (dockableComp.IsDocked) {
                btn.Load(IconId.CUndock, T.Undock, () => {
                    dock.Undock(true);
                    ReloadTab();
                });
                btn.SetInteractable(true);
            } else if (dockableComp.IsDockingNow || dockableComp.HasDockingGoal) {
                btn.Load(IconId.CDock, T.Dock, null);
                btn.SetInteractable(false);
            } else {
                btn.Load(IconId.CDock, T.Dock, () => {
                    dockableComp.TryOrderDocking();
                    ReloadTab();
                });
                btn.SetInteractable(true);
            }
        }

        private int AddAutoDockToggle(WGroup group, int idx) {
            if (!dock.IsAssigned) { return 0; }
            var beingData = S.Query.FindBeingData.Ask(dock.AssignedBeingDataId);
            if (beingData == null) { return 0; }
            var toggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WFooterItem_Toggle, group.Container, idx++);
            toggle.Load(T.AutoDock, !beingData.Dockable.NoAutoDock, isOn => {
                beingData.Dockable.NoAutoDock = !isOn;
                ReloadTab();
            });
            UIUtils.AddTip(lifetime, toggle, T.AutoDockTip);
            return idx;
        }

        protected override void Cleanup() {
            tile = null;
            dock = null;
            S = null;
            contentBtn = null;
            assignedBotRow = null;
            dockButton = null;
        }
    }
}
