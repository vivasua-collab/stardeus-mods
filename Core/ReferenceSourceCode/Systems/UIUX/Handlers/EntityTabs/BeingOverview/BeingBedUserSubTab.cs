using System.Buffers;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Sleep;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class BeingBedUserSubTab : BaseBeingSubTab {
        public BeingBedUserSubTab(EntityOverviewContext ctx) : base(ctx) { }
        private WButtonIconText2 wSubTab;
        private const string Id = "BedUser";

        public override bool Spawn(RectTransform container, int siblingIdx) {
            if (being.Being == null) { return false; }
            if (!being.HasNeed(NeedIdH.Sleep)) { return false; }
            if (!being.Being.HasComponent<BedUserComp>()) { return false; }
            // don't show dead unassigned beds
            if (being.HasStateFlag(BeingState.Dead) && S.Query.FindBed.Ask(being, false) == null) {
                return false;
            }
            if (!being.IsFriendly) { return false; }

            wSubTab = UISpawn.Widget<WButtonIconText2>(
                lifetime, UIPrefabId.WContent2Button, container, siblingIdx);

            S.Query.GetBedUserStateB.Ask(being.Id).Bind(lifetime, () => {
                UpdateSubTabButton();
                if (SubPanelWidget.IsOpenFor == Id) {
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.Close(false);
                    ShowSubPanel();
                    Coroutines.OnNextFrame(() => {
                        sp = S.UIQuery.GetEntitySubPanel.Ask();
                        sp.RepositionWidget(wSubTab, connectorOnly: true);
                    });
                }
            }, false);

            UpdateSubTabButton();

            if (SubPanelWidget.IsOpenFor == Id) {
                ShowSubPanel();
                Coroutines.OnNextFrame(() => {
                    var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
                    subPanel.RepositionWidget(wSubTab, connectorOnly: true);
                });
            }

            return true;
        }

        private void UpdateSubTabButton() {
            var bed = S.Query.FindBed.Ask(being, false);
            if (bed != null) {
                BedSlot slot = null;
                foreach (var sl in bed.Slots) {
                    if (sl.OwnerDataId == being.Id) {
                        slot = sl;
                        break;
                    }
                }
                var slotName = slot?.Name;
                wSubTab.Load(IconId.CSleep, T.NounBed,
                    Texts.Grey(slotName ?? T.AdjectiveAssigned), ShowSubPanel);
            } else {
                wSubTab.Load(IconId.CCross, T.NounBed,
                    Texts.Grey(T.NotAssigned), ShowSubPanel);
            }
        }

        private void ShowSubPanel() {
            if (being.Being == null) { return; }
            if (!being.Being.TryGetComponent<BedUserComp>(out _)) { return; }

            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(T.NounBed);
            int gIdx = 0;

            var bed = S.Query.FindBed.Ask(being, false);
            if (bed != null) {
                // Block 0: current assignment
                var assignedBlock = UISpawn.Block(l, subPanel.Container, gIdx++);
                int idx = 0;

                var header = UISpawn.Widget<WButtonText2>(l,
                    UIPrefabId.WContent2HeaderButton, assignedBlock.Container, idx++);
                header.Load(T.AssignedBed, () => {
                    if (S.Query.AssignBedSlot.Ask(being, null)) {
                        UISounds.PlayDigitalConfirm();
                    } else {
                        UISounds.PlayActionDenied();
                    }
                }, T.VerbUnassign);

                var bedBtn = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, assignedBlock.Container, idx++);
                bedBtn.Load(IconId.CSleep, EntityUtils.PosOrLabelOf(bed.Tile),
                    null, () => {
                    S.Sig.SelectEntity.Send(bed.Tile);
                });
                UIUtils.AddTipFunc(l, bedBtn,
                    () => Texts.LinkToTile(bed.Tile));
            }

            // Block for assignable beds list
            SpawnAssignableBedsBlock(l, subPanel, gIdx++);
            subPanel.Show(wSubTab, Id);
        }

        private void SpawnAssignableBedsBlock(Lifetime l,
                SubPanelWidget subPanel, int gIdx) {
            var block = UISpawn.Block(l, subPanel.Container, gIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.AvailableBeds);

            var bedCount = S.Components.CountByType<BedComp>();
            if (bedCount == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return;
            }

            var pool = ArrayPool<BedComp>.Shared;
            var bedArr = pool.Rent(bedCount * 3);
            var assignableCnt = S.Query.FindBeds.Ask(bedArr, being, true);

            if (assignableCnt == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                int added = 0;
                for (int i = 0; i < assignableCnt; i++) {
                    var bedComp = bedArr[i];
                    if (bedComp == null) { continue; }
                    var w = UISpawn.Widget<WContentButton2>(l,
                        UIPrefabId.WContent2Button2, block.Container, idx++);
                    w.Item.Load(IconId.CSleep,
                        EntityUtils.PosOrLabelOf(bedComp.Tile), () => {
                            EntityUtils.SelectOrFocus(bedComp.Tile);
                        });
                    w.Button.Load(T.VerbAssign, () => {
                        foreach (var sl in bedComp.Slots) {
                            if (sl.Owner == null && sl.IsCompatibleWith(being)) {
                                if (S.Query.AssignBedSlot.Ask(being, sl)) {
                                    UISounds.PlayDigitalConfirm();
                                } else {
                                    UISounds.PlayActionDenied();
                                }
                                return;
                            }
                        }
                        UISounds.PlayActionDenied();
                    });
                    UIUtils.AddTipFunc(l, w,
                        () => Texts.LinkToTile(bedComp.Tile));
                    added++;
                }
                if (added == 0) {
                    var empty = UISpawn.Widget<WIconText>(l,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    empty.Load(IconId.CInfo, T.Empty);
                }
            }

            pool.Return(bedArr, true);
        }
    }
}
