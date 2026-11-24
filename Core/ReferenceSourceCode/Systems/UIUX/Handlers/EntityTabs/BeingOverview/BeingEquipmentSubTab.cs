using System;
using System.Collections.Generic;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Systems.AI;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class BeingEquipmentSubTab : BaseBeingSubTab {
        public BeingEquipmentSubTab(EntityOverviewContext ctx) : base(ctx) {

        }
        private WContentListButton wSubTab;
        private Lifetime contentLifetime;
        private WButtonText policyButton;
        private PoliciesSys policies;
        [ClearOnReload]
        private static bool isExpanded;
        // Rows kept at each end of a long quality choice
        private const int QualityChoiceEnds = 10;

        public override bool Spawn(RectTransform container, int siblingIdx) {
            if (being.Inventory == null) { return false; }

            wSubTab = UISpawn.Widget<WContentListButton>(lifetime,
                UIPrefabId.WContent2ListButton, container, siblingIdx);
            wSubTab.Load(IconId.CShield, T.Equipment, being.Inventory.EquippedCount);
            wSubTab.LoadOnToggle(lifetime, ToggleContent, isExpanded);
            being.Inventory.EquipmentChangedB.Bind(lifetime, () => {
                wSubTab.SetText2(being.Inventory.EquippedCount);
                wSubTab.RebuildContainer();
            }, false);

            return true;
        }

        private bool ToggleContent(Lifetime life, RectTransform container, bool willShow, bool wasLoaded) {
            S.UIQuery.CloseEntitySubPanel.Ask();
            if (contentLifetime != null) {
                contentLifetime.Dispose();
                contentLifetime = null;
            }
            isExpanded = willShow;
            if (!willShow) { return wasLoaded; }
            contentLifetime = lifetime.CreateChild("Content");
            int idx = 0;
            var isDead = being.HasStateFlag(BeingState.Dead);
            var emptySlotVerb = isDead ? T.Empty : T.VerbEquip;
            SpawnPolicyButton(container, ref idx, isDead);
            foreach (var slot in being.Inventory.Equipment.Values.OrderBy(s => s.Item == null)) {
                if (slot.Item == null) {
                    var sw = UISpawn.Widget<WButtonText>(
                        contentLifetime, UIPrefabId.WContent2ButtonSimple, container, idx++);
                    sw.Load(Texts.Italic($"{slot.NameT}: {emptySlotVerb}"));
                    sw.SetOnClick(() => ShowSubPanelFor(sw, slot));
                    UIUtils.AddTipFunc(contentLifetime, sw, slot.SlotTooltip);
                } else {
                    var sw = UISpawn.Widget<WButtonIconText2>(
                        contentLifetime, UIPrefabId.WContent2Button, container, idx++);
                    sw.Load(slot.SlotIcon, ItemOwnership.DecoratedItemName(slot.ObjData, being, slot.ItemName));
                    if (slot.Item.ColorGroupId != null) {
                        sw.SetTint(slot.ObjData.Color);
                    }
                    sw.SetOnClick(() => ShowSubPanelFor(sw, slot));
                    sw.SetText2(Units.PercentageColored(slot.ObjData.Quality, clamp: false));
                    UIUtils.AddTipFunc(contentLifetime, sw, () => {
                        return ItemOwnership.OriginLabel(slot.ObjData, being);
                    });
                    UIUtils.AddCodexTooltip(contentLifetime, sw.gameObject, sw, slot.ObjData.Def);
                }
            }
            return true;
        }

        // Jumps to this being's Equipment policy. Only for beings the Policies panel
        // can actually list - a raider's or a corpse's gear is view-only, and a being
        // with no slots has nothing to configure.
        private void SpawnPolicyButton(RectTransform container, ref int idx, bool isDead) {
            if (isDead || !being.IsPlayer || being.IsGuest) { return; }
            if (being.Inventory.Equipment.Count == 0) { return; }
            policies ??= S.Sys.Get<PoliciesSys>(PoliciesSys.SysId);
            policyButton = UISpawn.Widget<WButtonText>(
                contentLifetime, UIPrefabId.WContent2ButtonSimple, container, idx++);
            policyButton.Load(PolicyLabel(), () => S.UIQuery.ShowPoliciesFor.Ask(being));
            // The Policies panel usually stays open next to this one, so the label
            // would go stale the moment the player switches the origin over there.
            S.Sig.EquipmentPolicyChanged.AddListener(OnPolicyChanged);
            contentLifetime.OnDispose(() => {
                S.Sig.EquipmentPolicyChanged.RemoveListener(OnPolicyChanged);
                policyButton = null;
            });
        }

        private void OnPolicyChanged() {
            policyButton?.SetText(PolicyLabel());
        }

        private string PolicyLabel() {
            return Texts.Italic(
                $"{T.PolicyActive}: {EquipmentPolicyTexts.OriginName(policies, being)}");
        }

        private void ShowStripQuickChoice(InventoryComp invComp) {
            var qc = QuickChoice.Create();
            if (invComp.HasGoal) {
                qc.AddButton($"{T.CancelTask}: {T.StripEquipment}", () => {
                    AIGoal.Cancel(invComp.Entity.Goal, G.Ticks, T.StripEquipment);
                    UIUtils.RefreshSelectedEntityUI();
                });
            } else {
                qc.AddButton(T.OrderStrip, () => {
                    invComp.OrderStripEquipment();
                    UIUtils.RefreshSelectedEntityUI();
                });
            }
            S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private void ShowSubPanelFor(WBase itemWidget, InventoryItemSlot slot) {
            var b = being.Being;
            if (b == null) {
                S.Sig.ShowErrorMessage.Send($"{T.NotAvailable}: {being.Mind.Name.DisplayName}");
                return;
            }

            if (b.IsDead && b.TryGetComponent<InventoryComp>(out var invComp)) {
                if (invComp.Inventory.HasRemovableEquipment || invComp.HasGoal) {
                    ShowStripQuickChoice(invComp);
                }
                return;
            }

            // Equipment of beings we don't control (raiders, guests/passengers, …) is
            // view-only — same as looking at a raider's gear. Mirrors the wrong-faction
            // guard in OrderEquip.
            if (!being.IsPlayer) {
                S.Sig.ShowErrorMessage.Send(T.DirectOrderWrongFaction);
                return;
            }

            if (being.Being?.AIAgent.DirectControl.IsControllable != true) {
                S.Sig.ShowErrorMessage.Send(T.NoDirectControlShort);
                return;
            }

            var res = Caches.ObjDataList;
            res.Clear();
            var found = S.UIQuery.FindCompatibleEquipmentUI.Ask(
                    b.AIAgent, slot.Type.IdH, res);

            var subPanel = S.UIQuery.GetEntitySubPanel.Ask();
            var l = subPanel.CreateLifetime(contentLifetime);
            subPanel.SetTitle($"{slot.SlotName}: {T.SelectItem}");

            var block = UISpawn.Block(l, subPanel.Container, 0);
            var cont = block.Container;

            int idx = 0;
            if (slot.Item != null) {
                // unequip button
                var cw = UISpawn.Widget<WButtonText>(l,
                    UIPrefabId.WContent2ButtonSimple, cont, idx++);
                cw.Load(Texts.Italic($"{slot.Item.ItemName}: {T.Unequip}"));
                cw.SetOnClick(() => {
                    if (slot.TryUnequipItem(S, being)) {
                        UISounds.PlayDigitalConfirm();
                        subPanel.Close();
                    }
                });
            }

            if (found == 0) {
                var cw = UISpawn.Widget<WIconText>(l,
                    UIPrefabId.WContent2Text, cont, idx++);
                cw.Load(IconId.CCross, $"{Texts.Italic(T.CompatibleItems)}: 0");
            } else {
                SpawnCompatibleOptions(l, cont, res, idx);
            }
            SpawnIncompatibleBlock(l, subPanel.Container, b.AIAgent, slot);
            subPanel.Show(itemWidget, null);
        }

        // Items this being can't use (a robot and a sniper rifle, or a gun with Shooting
        // incapable), listed so they don't just silently go missing from the choices.
        // Rows are not clickable; hovering one shows the item's codex tooltip.
        private void SpawnIncompatibleBlock(Lifetime l, WGroup container,
                AIAgentComp agent, InventoryItemSlot slot) {
            var incompatible = new List<ObjData>();
            var found = S.UIQuery.FindIncompatibleEquipmentUI.Ask(
                agent, slot.Type.IdH, incompatible);
            if (found == 0) { return; }
            var block = UISpawn.Block(l, container, 1);
            int idx = 0;
            var header = UISpawn.Widget<WText>(l, UIPrefabId.WContentHeader,
                block.Container, idx++);
            header.Load(T.AdjIncompatible);
            foreach (var group in incompatible
                    .GroupBy(obj => obj.Item)
                    .OrderBy(g => g.Key.ItemName)) {
                var item = group.Key;
                var row = UISpawn.Widget<WIconText2>(l, UIPrefabId.WContentText2,
                    block.Container, idx++);
                row.Load(item.Def.Preview, ItemLabel(item, group.Count()),
                    item.IncompatibilityReason(being));
                UIUtils.AddCodexTooltip(l, row.gameObject, row, item.Def);
            }
        }

        private void SpawnCompatibleOptions(Lifetime l, RectTransform cont, List<ObjData> res,
                int idx) {
            var defGroups = res
                .GroupBy(obj => obj.Item)
                .Select(g => {
                    var items = g.ToList();
                    var colorsMap = new Dictionary<Color, List<ObjData>>();
                    foreach (var it in items) {
                        if (it.Item.ColorGroupId != null) {
                            if (!colorsMap.TryGetValue(it.Color, out var cl)) {
                                cl = new List<ObjData>();
                                colorsMap[it.Color] = cl;
                            }
                            cl.Add(it);
                        }
                    }
                    return (first: items[0], items, colors: colorsMap);
                })
                .OrderByDescending(g => QualityMax(g.items))
                .ThenBy(g => g.first.Def.NameT);

            foreach (var (first, items, colors) in defGroups) {
                SpawnItemOption(l, cont, first, colors, items, idx++);
            }
        }

        private void SpawnItemOption(Lifetime l, RectTransform cont, ObjData item,
                Dictionary<Color, List<ObjData>> colors, List<ObjData> defItems, int idx) {
            var itemDef = item.Item;
            var hasColor = itemDef.ColorGroupId != null;
            if (hasColor) {
                if (colors.Count > 1) {
                    // multi color choice!
                    var w = UISpawn.Widget<WContentListButton>(l,
                        UIPrefabId.WContent2ListButton, cont, idx);
                    w.Load(itemDef.Def.Preview, ItemOwnership.OriginLabelFor(defItems, being, ItemLabel(itemDef, defItems.Count)),
                        QualityRangeLabel(defItems));
                    UIUtils.AddCodexTooltip(l, w.gameObject, w, itemDef.Def);
                    var capColors = colors
                        .OrderByDescending(kv => QualityMax(kv.Value))
                        .ToArray();
                    w.LoadOnToggle(l, (life, container, willShow, wasLoaded) => {
                        if (wasLoaded || !willShow) { return wasLoaded; }
                        int colIdx = 0;
                        foreach (var c in capColors) {
                            var colorItems = c.Value;
                            var cw = UISpawn.Widget<WButtonIconText2>(l,
                                UIPrefabId.WContent2Button, container, colIdx++);
                            cw.Load(item.Def.Preview, ItemOwnership.OriginLabelFor(
                                colorItems, being, ItemLabel(itemDef, colorItems.Count)));
                            cw.SetTint(c.Key);
                            cw.SetText2(QualityRangeLabel(colorItems));
                            cw.SetOnClick(() => OnEquipGroupClicked(colorItems));
                        }
                        return true;
                    });
                    return;
                } else {
                    // one color
                    var kv = colors.First();
                    var colorItems = kv.Value;
                    var cw = UISpawn.Widget<WButtonIconText2>(l,
                        UIPrefabId.WContent2Button, cont, idx);
                    cw.Load(item.Def.Preview, ItemOwnership.OriginLabelFor(
                        colorItems, being, ItemLabel(itemDef, colorItems.Count)));
                    cw.SetTint(kv.Key);
                    cw.SetText2(QualityRangeLabel(colorItems));
                    cw.SetOnClick(() => OnEquipGroupClicked(colorItems));
                }
            } else {
                var cw = UISpawn.Widget<WButtonIconText2>(l,
                    UIPrefabId.WContent2Button, cont, idx);
                cw.Load(item.Def.Preview, ItemOwnership.OriginLabelFor(
                    defItems, being, ItemLabel(itemDef, defItems.Count)));
                cw.SetText2(QualityRangeLabel(defItems));
                cw.SetOnClick(() => OnEquipGroupClicked(defItems));
            }
        }

        private void OnEquipGroupClicked(List<ObjData> items) {
            if (items.Count == 1) {
                OrderEquip(items[0]);
            } else {
                ShowEquipQualityChoice(items);
            }
        }

        // A big ship holds hundreds of one item, far more rows than fit on screen and slow
        // to spawn. Items with the same label are one choice to the player, and the picks
        // worth making are at the ends, so the middle collapses into one count row.
        private void ShowEquipQualityChoice(List<ObjData> items) {
            var rows = new List<(string label, ObjData item, int count)>();
            var rowIdx = new Dictionary<string, int>();
            foreach (var item in items.OrderByDescending(o => o.Quality)) {
                var label = ItemOwnership.DecoratedItemName(item, being,
                    $"{T.NounQuality}: {Units.PercentageColored(item.Quality, clamp: false)}",
                    markNew: true);
                if (rowIdx.TryGetValue(label, out var ri)) {
                    var r = rows[ri];
                    rows[ri] = (r.label, r.item, r.count + 1);
                    continue;
                }
                rowIdx[label] = rows.Count;
                rows.Add((label, item, 1));
            }
            var qc = QuickChoice.Create(T.VerbEquip);
            var collapse = rows.Count > QualityChoiceEnds * 2;
            for (int i = 0; i < rows.Count; i++) {
                if (collapse && i == QualityChoiceEnds) {
                    var last = rows.Count - QualityChoiceEnds - 1;
                    qc.AddLabel(HiddenRowsLabel(rows, i, last));
                    i = last;
                    continue;
                }
                var (label, item, count) = rows[i];
                qc.AddButton(count > 1 ? $"{label} {Units.XNum(count)}" : label,
                    () => OrderEquip(item), tip: ItemOwnership.OriginLabel(item, being));
            }
            S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        private static string HiddenRowsLabel(List<(string label, ObjData item, int count)> rows,
                int from, int to) {
            int hidden = 0;
            for (int i = from; i <= to; i++) {
                hidden += rows[i].count;
            }
            // Rows are sorted best first, and each row's item is the best of its label
            var min = Units.PercentageColored(rows[to].item.Quality, clamp: false);
            var max = Units.PercentageColored(rows[from].item.Quality, clamp: false);
            return Texts.Italic($"... {Units.XNum(hidden)} {min}-{max} ...");
        }

        // The picker's hover tooltip is the shared per-Def codex panel, so it cannot
        // carry per-item facts. Provenance goes on the row itself instead, where it
        // is visible without hovering at all.
        // private string QualityAndOriginLabel(List<ObjData> items) {
        //     return WithOrigin(QualityRangeLabel(items),
        //         ItemOwnership.OriginLabelFor(items, being));
        // }

        // private static string WithOrigin(string label, string origin) {
        //     return origin == null ? label : $"{label} {origin}";
        // }

        private static string QualityRangeLabel(List<ObjData> items) {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var it in items) {
                if (it.Quality < min) { min = it.Quality; }
                if (it.Quality > max) { max = it.Quality; }
            }
            return min == max
                ? Units.PercentageColored(min, clamp: false)
                : $"{Units.PercentageColored(min, clamp: false)}-{Units.PercentageColored(max, clamp: false)}";
        }

        private static float QualityMax(List<ObjData> items) {
            float max = float.MinValue;
            foreach (var it in items) {
                if (it.Quality > max) { max = it.Quality; }
            }
            return max;
        }

        private static string ItemLabel(InventoryItemDef def, int count) {
            return count > 1 ? $"{def.ItemName} {Units.XNum(count)}" : def.ItemName;
        }

        private void OrderEquip(ObjData item) {
            var o = being;
            if (!o.IsPlayer) {
                S.Sig.ShowErrorMessage.Send(T.DirectOrderWrongFaction);
                var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
                subPanel.Close();
                return;
            }
            if (o.Being == null) {
                S.Sig.ShowErrorMessage.Send($"{o.Mind.LinkToShortName}: {T.NotAvailable}");
                var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
                subPanel.Close();
                return;
            }
            if (!o.Being.AIAgent.DirectControl.IsControllable) {
                S.Sig.ShowErrorMessage.Send(T.NoDirectControlShort);
                var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
                subPanel.Close();
                return;
            }
            if (o.Being.Motion.IsImmobilized) {
                S.Sig.ShowErrorMessage.Send(T.InfoBeingImmobilized);
                var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
                subPanel.Close();
                return;
            }
            if (BeingUtils.TryOrderEquipItem(being.Being, item)) {
                UISounds.PlayDigitalConfirm();
                var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
                subPanel.Close();
            } else {
                UISounds.PlayActionDenied();
                D.Err("TODO rebuild list");
            }
        }

        public override void Cleanup() {
        }
    }
}
