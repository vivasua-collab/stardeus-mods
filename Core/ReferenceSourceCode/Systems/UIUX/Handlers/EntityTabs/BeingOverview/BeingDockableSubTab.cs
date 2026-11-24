using System.Buffers;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class BeingDockableSubTab : BaseBeingSubTab {
        public BeingDockableSubTab(EntityOverviewContext ctx) : base(ctx) { }
        private WButtonIconText2 wSubTab;
        private const string Id = "Dockable";

        public override bool Spawn(RectTransform container, int siblingIdx) {
            if (being.Being == null) { return false; }
            if (!being.Being.TryGetComponent<BotDockableComp>(out var dockable)) { return false; }

            wSubTab = UISpawn.Widget<WButtonIconText2>(
                lifetime, UIPrefabId.WContent2Button, container, siblingIdx);

            S.Query.GetDockableStateB.Ask(being.Id).Bind(lifetime, () => {
                UpdateSubTabButton(dockable);
                if (SubPanelWidget.IsOpenFor == Id) {
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.Close(false);
                    ShowSubPanel();
                    var capturedLife = lifetime;
                    Coroutines.OnNextFrame(() => {
                        // the sub tab may have closed meanwhile, and its cleanup nulls S
                        if (capturedLife == null || capturedLife.IsDisposed) { return; }
                        sp = S.UIQuery.GetEntitySubPanel.Ask();
                        sp.RepositionWidget(wSubTab, connectorOnly: true);
                    });
                }
            }, false);

            UpdateSubTabButton(dockable);

            if (SubPanelWidget.IsOpenFor == Id) {
                ShowSubPanel();
                var capturedLife = lifetime;
                Coroutines.OnNextFrame(() => {
                    if (capturedLife == null || capturedLife.IsDisposed) { return; }
                    var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
                    subPanel.RepositionWidget(wSubTab, connectorOnly: true);
                });
            }

            return true;
        }

        private void UpdateSubTabButton(BotDockableComp dockable) {
            var assignedDock = dockable.AssignedDock;
            if (assignedDock != null) {
                string statusIcon;
                string text2;
                if (dockable.IsDocked) {
                    statusIcon = IconId.CDock;
                    text2 = T.Docked;
                } else if (dockable.IsDockingNow || dockable.HasDockingGoal) {
                    statusIcon = IconId.CDock;
                    text2 = T.Docking;
                } else {
                    statusIcon = IconId.CUndock;
                    text2 = T.NotDocked;
                }
                wSubTab.Load(statusIcon, T.Dock, Texts.Grey(text2), ShowSubPanel);
            } else {
                wSubTab.Load(IconId.CCross, T.Dock, Texts.Grey(T.NotAssigned), ShowSubPanel);
            }
        }

        private void ShowSubPanel() {
            if (being.Being == null) { return; }
            if (!being.Being.TryGetComponent<BotDockableComp>(out var dockable)) { return; }

            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.Dock);
            int gIdx = 0;

            var assignedDock = dockable.AssignedDock;
            if (assignedDock != null) {
                // Block 0: current assignment
                var assignedBlock = UISpawn.Block(l, subPanel.Container, gIdx++);
                int idx = 0;

                var header = UISpawn.Widget<WButtonText2>(l,
                    UIPrefabId.WContent2HeaderButton, assignedBlock.Container, idx++);
                header.Load(T.AssignedDock, () => {
                    if (S.Query.AssignBotDock.Ask(being, null)) {
                        UISounds.PlayDigitalConfirm();
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }, T.VerbUnassign);

                var dockBtn = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, assignedBlock.Container, idx++);
                dockBtn.Load(IconId.CDock, EntityUtils.PosOrLabelOf(assignedDock.Tile),
                    null, () => {
                    S.Sig.SelectEntity.Send(assignedDock.Tile);
                });
                UIUtils.AddTipFunc(l, dockBtn,
                    () => Texts.LinkToTile(assignedDock.Tile));
            }

            // Block for assignable docks list
            SpawnAssignableDocksBlock(l, subPanel, gIdx++);
            subPanel.Show(wSubTab, Id);
        }

        private void SpawnAssignableDocksBlock(Lifetime l,
                SubPanelWidget subPanel, int gIdx) {
            var block = UISpawn.Block(l, subPanel.Container, gIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.AssignableDocks);

            var pool = ArrayPool<BotDockComp>.Shared;
            var dockArr = pool.Rent(64);
            var assignableCnt = S.Query.FindDocks.Ask(dockArr, true);

            if (assignableCnt == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                int added = 0;
                for (int i = 0; i < assignableCnt; i++) {
                    var dock = dockArr[i];
                    if (dock == null) { continue; }
                    var w = UISpawn.Widget<WContentButton2>(l,
                        UIPrefabId.WContent2Button2, block.Container, idx++);
                    w.Item.Load(IconId.CDock,
                        EntityUtils.PosOrLabelOf(dock.Tile), () => {
                            EntityUtils.SelectOrFocus(dock.Tile);
                        });
                    w.Button.Load(T.VerbAssign, () => {
                        if (S.Query.AssignBotDock.Ask(being, dock)) {
                            UISounds.PlayDigitalConfirm();
                        } else {
                            UISounds.PlayActionDenied();
                        }
                    });
                    UIUtils.AddTipFunc(l, w,
                        () => Texts.LinkToTile(dock.Tile));
                    added++;
                }
                if (added == 0) {
                    var empty = UISpawn.Widget<WIconText>(l,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    empty.Load(IconId.CInfo, T.Empty);
                }
            }

            pool.Return(dockArr, true);
        }
    }
}
