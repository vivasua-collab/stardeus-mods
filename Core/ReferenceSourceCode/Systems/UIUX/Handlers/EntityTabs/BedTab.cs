#if UNITY_EDITOR
#define DEBUG_SLOT_NAMES
#endif
using System.Buffers;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Creatures;
using Game.Systems.Drama;
using Game.Systems.Mental;
using Game.Systems.Sleep;
using Game.Systems.Stats;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class BedTab : BaseEntityTab {
        public const string TabId = "Bed";

        [SelfInit]
        private static void Register() {
            RegisterTab(new BedTab());
        }

        private const string SubId = "BedSlot";
        private Tile tile;
        private BedComp bed;
        private GameState S;
        private int activeSlotIdx;
        private WBase activeSlotWidget;

        public override string Id => TabId;
        public override int Order => 10;
        public override string TabName => T.SleepingSlots;

        public override bool ShowFor(GameState s, Tile tile) {
            S = s;
            if (!tile.IsConstructed) { return false; }
            if (!tile.TryGetComponent<BedComp>(out var bc)) { return false; }
            this.tile = tile;
            this.bed = bc;
            return true;
        }

        public override bool LoadContent(WGroup group) {
            bed.StateChangedB.Bind(lifetime, () => ReloadTab(), false);

            int gIdx = 0;
            bool hasHumanSlots = false;
            bool hasPetSlots = false;

            for (int i = 0; i < bed.NumSlots; i++) {
                var slot = bed.Slots[i];
                if ((slot.Compatibility & StatFlags.Pet) == StatFlags.Pet) {
                    hasPetSlots = true;
                } else {
                    hasHumanSlots = true;
                }
            }

            if (hasHumanSlots) {
                var humanBlock = UISpawn.Block(lifetime, group, gIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, humanBlock.Container, idx++);
                header.Load(T.SleepingSlots);

                for (int i = 0; i < bed.NumSlots; i++) {
                    var slot = bed.Slots[i];
                    if ((slot.Compatibility & StatFlags.Pet) == StatFlags.Pet) {
                        continue;
                    }
                    LoadSlotWidget(humanBlock.Container, ref idx, slot, i);
                }
            }

            if (hasPetSlots) {
                var petBlock = UISpawn.Block(lifetime, group, gIdx++);
                int idx = 0;
                var header = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2Header, petBlock.Container, idx++);
                header.Load(T.SleepingSlotsPets);

                for (int i = 0; i < bed.NumSlots; i++) {
                    var slot = bed.Slots[i];
                    if ((slot.Compatibility & StatFlags.Pet) != StatFlags.Pet) {
                        continue;
                    }
                    LoadSlotWidget(petBlock.Container, ref idx, slot, i);
                }
            }

            if (SubPanelWidget.IsOpenFor == SubId && activeSlotWidget != null) {
                var slot = bed.Slots[activeSlotIdx];
                if (slot.IsTemporary) {
                    // no sub-panel for temporary slots
                } else if (slot.Owner != null) {
                    ShowAssignedSlotSubPanel(activeSlotWidget, slot);
                } else {
                    ShowAssignSubPanel(activeSlotWidget, slot);
                }
                var btn = activeSlotWidget;
                var capturedLife = lifetime;
                Coroutines.OnNextFrame(() => {
                    // the tab may have closed meanwhile, and its cleanup nulls S
                    if (capturedLife == null || capturedLife.IsDisposed) { return; }
                    var sp = S.UIQuery.GetEntitySubPanel.Ask();
                    sp.RepositionWidget(btn, connectorOnly: true);
                });
            }

            #if DEBUG_SLOT_NAMES
            LoadDebugNameOffsetSliders(group, ref gIdx);
            #endif

            return true;
        }

        private void LoadSlotWidget(RectTransform container, ref int idx,
                BedSlot slot, int slotIdx) {
            if (slot.IsTemporary) {
                var w = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, container, idx++);
                w.Load(IconId.CTime, $"{slot.Name} {Texts.GreyParentheses(T.AdjTemporary)}");
                return;
            }

            if (slot.Owner != null) {
                var beingData = slot.Owner;
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, container, idx++);
                if (slotIdx == activeSlotIdx) {
                    activeSlotWidget = w;
                }
                w.Load(null, BeingUtils.DecoratedBeingName(beingData, false), Texts.Grey(slot.Name), () => {
                    activeSlotIdx = slotIdx;
                    activeSlotWidget = w;
                    ShowAssignedSlotSubPanel(w, slot);
                });
                UIUtils.AddTipFunc(lifetime, w, () => GetLoverInfo(beingData.Mind));
                beingData.Avatar.Load(w.Image);
            } else {
                var w = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, container, idx++);
                if (slotIdx == activeSlotIdx) {
                    activeSlotWidget = w;
                }
                w.Load(IconId.CCross, T.NotAssigned,
                    Texts.Grey(slot.Name), () => {
                    activeSlotIdx = slotIdx;
                    activeSlotWidget = w;
                    ShowAssignSubPanel(w, slot);
                });
            }
        }

        private void ShowAssignedSlotSubPanel(WBase forWidget, BedSlot slot) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(slot.Name);
            int gIdx = 0;

            // Block 0: current assignment with unassign action
            var assignedBlock = UISpawn.Block(l, subPanel.Container, gIdx++);
            int idx = 0;

            var assignedHeader = UISpawn.Widget<WButtonText2>(l,
                UIPrefabId.WContent2HeaderButton, assignedBlock.Container, idx++);
            assignedHeader.Load(T.AdjectiveAssigned, () => {
                S.Query.AssignBedSlot.Ask(slot.Owner, null);
                subPanel.Close();
                ShowAssignSubPanel(forWidget, slot);
            }, T.VerbUnassign);

            var assignedBtn = UISpawn.Widget<WButtonIconText2>(l,
                UIPrefabId.WContent2Button, assignedBlock.Container, idx++);
            assignedBtn.Load(null, BeingUtils.DecoratedBeingName(slot.Owner, false), null, () => {
                BeingUtils.SelectFocused(slot.Owner);
            });
            UIUtils.AddTipFunc(lifetime, assignedBtn, () => GetLoverInfo(slot.Owner.Mind));
            slot.Owner.Avatar.Load(assignedBtn.Image);

            // Block 1: unassigned beings
            SpawnAssignableBeingsBlock(l, subPanel, gIdx++, slot);
            // Block 2: beings assigned to other beds
            SpawnAssignedBeingsBlock(l, subPanel, gIdx++, slot);
            LoadAutoAssignSubPanelFooter(l, subPanel, slot);
            subPanel.Show(forWidget, SubId);
        }

        private void ShowAssignSubPanel(WBase forWidget, BedSlot slot) {
            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(lifetime);
            subPanel.SetTitle(slot.Name);

            int gIdx = 0;

            SpawnAssignableBeingsBlock(l, subPanel, gIdx++, slot);
            SpawnAssignedBeingsBlock(l, subPanel, gIdx++, slot);
            LoadAutoAssignSubPanelFooter(l, subPanel, slot);
            subPanel.Show(forWidget, SubId);
        }

        private void LoadAutoAssignSubPanelFooter(Lifetime l,
                SubPanelWidget subPanel, BedSlot slot) {
            var toggle = UISpawn.Widget<WToggleText>(l,
                UIPrefabId.WFooterItem_Toggle, subPanel.Footer.Container, 0);
            toggle.Load(T.AutoAssign, slot.AutoAssign, isOn => {
                slot.AutoAssign = isOn;
            });
            UIUtils.AddTip(l, toggle, T.AutoAssignSlotTip);
            subPanel.ShowFooter(true);
        }

        private void SpawnAssignableBeingsBlock(Lifetime l,
                SubPanelWidget subPanel, int gIdx, BedSlot slot) {
            var block = UISpawn.Block(l, subPanel.Container, gIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.BeingsWithoutBed);

            var numBeings = S.Query.AllBeingData.Ask().Count;
            if (numBeings == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return;
            }

            var pool = ArrayPool<BeingData>.Shared;
            var beingArr = pool.Rent(numBeings);
            var assignableCnt = S.Query.FindBedUsers.Ask(beingArr, slot, true);

            if (assignableCnt == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            } else {
                int added = 0;
                for (int i = 0; i < assignableCnt; i++) {
                    var bd = beingArr[i];
                    if (bd == null) { continue; }
                    if (!bd.EffectiveFaction.IsPlayer && !bd.IsGuest) { continue; }
                    var w = UISpawn.Widget<WContentButton2>(l,
                        UIPrefabId.WContent2Button2, block.Container, idx++);
                    w.Item.Load(null, BeingUtils.DecoratedBeingName(bd, false), () => {
                        BeingUtils.Select(bd, true);
                    });
                    w.Button.Load(T.VerbAssign, () => {
                        S.Query.AssignBedSlot.Ask(bd, slot);
                        subPanel.Close();
                    });
                    UIUtils.AddTipFunc(lifetime, w, () => GetLoverInfo(bd.Mind));
                    bd.Avatar.Load(w.Item.Image);
                    added++;
                }
                if (added == 0) {
                    var empty = UISpawn.Widget<WIconText>(l,
                        UIPrefabId.WContent2Text, block.Container, idx++);
                    empty.Load(IconId.CInfo, T.Empty);
                }
            }

            pool.Return(beingArr, true);
        }

        private void SpawnAssignedBeingsBlock(Lifetime l,
                SubPanelWidget subPanel, int gIdx, BedSlot slot) {
            var block = UISpawn.Block(l, subPanel.Container, gIdx);
            int idx = 0;

            var header = UISpawn.Widget<WText>(l,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load(T.BeingsWithBed);

            var numBeings = S.Query.AllBeingData.Ask().Count;
            if (numBeings == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
                return;
            }

            var pool = ArrayPool<BeingData>.Shared;
            var beingArr = pool.Rent(numBeings);
            // get all compatible beings (not just assignable)
            var allCnt = S.Query.FindBedUsers.Ask(beingArr, slot, false);

            int added = 0;
            for (int i = 0; i < allCnt; i++) {
                var bd = beingArr[i];
                if (bd == null) { continue; }
                if (!bd.EffectiveFaction.IsPlayer && !bd.IsGuest) { continue; }
                // Skip unassigned beings (they appear in the other block)
                var theirBed = S.Query.FindBed.Ask(bd, false);
                if (theirBed == null) { continue; }
                // Skip beings already assigned to this bed's slot
                bool isOnThisSlot = false;
                foreach (var sl in bed.Slots) {
                    if (sl.OwnerDataId == bd.Id) {
                        isOnThisSlot = true;
                        break;
                    }
                }
                if (isOnThisSlot) { continue; }

                var w = UISpawn.Widget<WContentButton2>(l,
                    UIPrefabId.WContent2Button2, block.Container, idx++);
                w.Item.Load(null, BeingUtils.DecoratedBeingName(bd, false), () => {
                    BeingUtils.Select(bd, true);
                });
                w.Button.Load(T.VerbReassign, () => {
                    S.Query.AssignBedSlot.Ask(bd, slot);
                    subPanel.Close();
                });
                UIUtils.AddTipFunc(lifetime, w, () => GetLoverInfo(bd.Mind));
                bd.Avatar.Load(w.Item.Image);
                added++;
            }
            if (added == 0) {
                var empty = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                empty.Load(IconId.CInfo, T.Empty);
            }

            pool.Return(beingArr, true);
        }
        private string GetLoverInfo(Mind user) {
            if (user == null) { return null; }
            var bonds = user.Bonds;
            if (bonds == null) { return null; }
            var sb = Caches.StringBuilder;
            sb.Clear();
            if (bonds.Important.TryGetValue(BeingOpinionFlags.Spouse, out var spouses) && spouses.Length > 0) {
                sb.Append(Texts.SubTitle(T.StatusSpouse));
                for (int i = 0; i < spouses.Length; i++) {
                    var spouse = spouses[i];
                    sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                    sb.Append(' ');
                    if (spouse.Owner?.HasStateFlag(BeingState.Alive) == true) {
                        sb.AppendLine(spouse.Name.FullNameOrNameless);
                    } else {
                        sb.Append(GlyphMap.SpriteSkull);
                        sb.Append(' ');
                        sb.AppendLine(Texts.GreyStrike(spouse.Name.FullNameOrNameless));
                    }
                }
            }
            if (bonds.Important.TryGetValue(BeingOpinionFlags.Lover, out var lovers) && lovers.Length > 0) {
                sb.Append(Texts.SubTitle(T.StatusLovers));
                for (int i = 0; i < lovers.Length; i++) {
                    var lover = lovers[i];
                    sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                    sb.Append(' ');
                    if (lover.Owner?.HasStateFlag(BeingState.Alive) == true) {
                        sb.AppendLine(lover.Name.FullNameOrNameless);
                    } else {
                        sb.Append(GlyphMap.SpriteSkull);
                        sb.Append(' ');
                        sb.AppendLine(Texts.GreyStrike(lover.Name.FullNameOrNameless));
                    }
                }
            }
            if (bonds.Important.TryGetValue(BeingOpinionFlags.Nemesis, out var nemesis) && nemesis.Length > 0) {
                sb.Append(Texts.SubTitle(T.StatusNemesis));
                for (int i = 0; i < nemesis.Length; i++) {
                    var enemy = nemesis[i];
                    sb.Append(Texts.Accented(GlyphMap.ArrowRight));
                    sb.Append(' ');
                    if (enemy.Owner?.HasStateFlag(BeingState.Alive) == true) {
                        sb.AppendLine(enemy.Name.FullNameOrNameless);
                    } else {
                        sb.Append(GlyphMap.SpriteSkull);
                        sb.Append(' ');
                        sb.AppendLine(Texts.GreyStrike(enemy.Name.FullNameOrNameless));
                    }
                }
            }
            if (sb.Length > 0) {
                return sb.ToString();
            } else {
                return null;
            }
        }

        #if DEBUG_SLOT_NAMES
        private void LoadDebugNameOffsetSliders(WGroup group, ref int gIdx) {
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;
            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load($"[DEBUG] Name Offsets {bed.Tile.Transform.Rotation}");

            for (int i = 0; i < bed.NumSlots; i++) {
                var slot = bed.Slots[i];
                var capturedSlot = slot;
                var facing = bed.Tile.Transform.Rotation;

                var slotHeader = UISpawn.Widget<WText>(lifetime,
                    UIPrefabId.WContent2TextBlock, block.Container, idx++);
                slotHeader.Load(Texts.Bold($"{slot.Name} ({facing})"));

                var currentOffset = capturedSlot.NameOffset;

                var xRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, block.Container, idx++);
                xRow.Item.Load(IconId.CTransparent, $"Slot {i} X", $"{currentOffset.x:F2}");
                xRow.Slider.Load(currentOffset.x, v => {
                    SetNameOffset(capturedSlot, facing, new Vector2(v, capturedSlot.NameOffset.y));
                }, -2f, 2f);
                xRow.Slider.SetValueTextFunc(v => $"{v:F2}");

                var yRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, block.Container, idx++);
                yRow.Item.Load(IconId.CTransparent, $"Slot {i} Y", $"{currentOffset.y:F2}");
                yRow.Slider.Load(currentOffset.y, v => {
                    SetNameOffset(capturedSlot, facing, new Vector2(capturedSlot.NameOffset.x, v));
                }, -2f, 2f);
                yRow.Slider.SetValueTextFunc(v => $"{v:F2}");

                var rotRow = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                    UIPrefabId.WContent2Text2Slider, block.Container, idx++);
                rotRow.Item.Load(IconId.CTransparent, $"Slot {i} Rot", $"{capturedSlot.NameRotation:F0}°");
                rotRow.Slider.Load(capturedSlot.NameRotation, v => {
                    SetNameRotation(capturedSlot, facing, v);
                }, -180f, 180f);
                rotRow.Slider.SetValueTextFunc(v => $"{v:F0}°");
            }
        }

        private void SetNameOffset(BedSlot slot, Facing.Type facing, Vector2 value) {
            switch (facing) {
                case Facing.Type.Down:  slot.NameOffset_D = value; break;
                case Facing.Type.Up:    slot.NameOffset_U = value; break;
                case Facing.Type.Left:  slot.NameOffset_L = value; break;
                case Facing.Type.Right: slot.NameOffset_R = value; break;
            }
            bed.UpdateUIBlock(true);
        }

        private void SetNameRotation(BedSlot slot, Facing.Type facing, float value) {
            switch (facing) {
                case Facing.Type.Down:  slot.NameRotation_D = value; break;
                case Facing.Type.Up:    slot.NameRotation_U = value; break;
                case Facing.Type.Left:  slot.NameRotation_L = value; break;
                case Facing.Type.Right: slot.NameRotation_R = value; break;
            }
            bed.UpdateUIBlock(true);
        }
        #endif

        protected override void Cleanup() {
            tile = null;
            bed = null;
            S = null;
            activeSlotWidget = null;
        }
    }
}
