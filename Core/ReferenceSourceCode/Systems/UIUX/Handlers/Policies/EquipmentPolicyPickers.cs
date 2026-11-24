using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.Utils;

namespace Game.Systems.UIUX {
    // "Which colour" / "which item" choices for a slot policy. Vanilla has at most a
    // handful of items per slot, so a QuickChoice popup is the right control here.
    public static class EquipmentPolicyPickers {
        // current is what the row currently shows; getEditable is resolved only when
        // the player actually picks something, so merely opening the popup never
        // promotes a scope to having its own policy.
        public static void ShowColorPicker(PolicyTabContext ctx, WBase anchor,
                InventorySlotType type, EquipmentSlotPolicy current,
                Func<EquipmentSlotPolicy> getEditable, Action onPicked) {
            var qc = QuickChoice.Create(T.NounColor);
            qc.Widget = anchor.Rect;
            // The colour wheel reads as "any colour"; an untinted swatch would just
            // look like one more (white) option in the list below.
            qc.AddIconButton(IconId.CColorWheel, T.PronounAny, () => {
                getEditable().ColorId = null;
                onPicked();
            });
            foreach (var color in ColorsFor(ctx, type, current.ItemDefId)) {
                var capId = color.Id;
                qc.AddIconButton(color.NameT, new QuickChoice.IconButton() {
                    Icon = Images.Sprite(IconId.CColorPreview),
                    Tint = color.Color,
                    AutoClose = true,
                    OnClick = () => {
                        getEditable().ColorId = capId;
                        onPicked();
                    },
                });
            }
            ctx.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public static void ShowItemPicker(PolicyTabContext ctx, WBase anchor,
                InventorySlotType type, EquipmentSlotPolicy current,
                Func<EquipmentSlotPolicy> getEditable, Action onPicked) {
            var qc = QuickChoice.Create(T.PolicyItem);
            qc.Widget = anchor.Rect;
            qc.AddButton(T.PronounAny, () => {
                var editable = getEditable();
                editable.ItemDefId = null;
                // The old colour may belong to a group the new item can't have.
                editable.ColorId = null;
                onPicked();
            });
            foreach (var item in ItemsFor(ctx, type)) {
                var capItem = item;
                // The list offers anything researched, including items the colony has
                // none of, so say how many there are before the player pins one.
                var owned = T.AvailableCount_V1.F(
                    Units.Num(ctx.S.Query.ObjCountByDef.Ask(item.Def)));
                qc.AddIconButton(item.Def.Preview, item.ItemName, () => {
                    var editable = getEditable();
                    editable.ItemDefId = capItem.Id;
                    editable.ColorId = null;
                    onPicked();
                }, owned);
            }
            ctx.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        // Every colour any item in this slot can come in. Narrowed to one item def
        // when the policy already pins one.
        public static List<ColorDef> ColorsFor(PolicyTabContext ctx, InventorySlotType type,
                string itemDefId) {
            var result = new List<ColorDef>();
            var seenGroups = new HashSet<string>();
            foreach (var item in InventoryItemDef.All.Values) {
                if (item.SlotType != type) { continue; }
                if (item.ColorGroupId == null) { continue; }
                if (itemDefId != null && item.Id != itemDefId) { continue; }
                if (!IsVisible(ctx, item)) { continue; }
                if (!seenGroups.Add(item.ColorGroupId)) { continue; }
                var group = Palette.ForGroup(item.ColorGroupId);
                if (group == null) { continue; }
                foreach (var color in group) {
                    if (!result.Contains(color)) { result.Add(color); }
                }
            }
            return result;
        }

        public static List<InventoryItemDef> ItemsFor(PolicyTabContext ctx,
                InventorySlotType type) {
            var result = new List<InventoryItemDef>();
            foreach (var item in InventoryItemDef.All.Values) {
                if (item.SlotType != type) { continue; }
                if (!IsVisible(ctx, item)) { continue; }
                var body = BodyOf(ctx);
                if (body != null && !item.IsAllowedFor(body, FactionDef.Player)) { continue; }
                result.Add(item);
            }
            result.Sort(CompareItems);
            return result;
        }

        // The body the scope's policy is for: null at the global scope, which covers
        // every species and so offers every item.
        public static Species BodyOf(PolicyTabContext ctx) {
            return ctx.Scope switch {
                PolicyScope.Being => ctx.Being?.Species,
                PolicyScope.Species => ctx.Species,
                _ => null,
            };
        }

        public static bool HasColorChoice(PolicyTabContext ctx, InventorySlotType type,
                string itemDefId) {
            return ColorsFor(ctx, type, itemDefId).Count > 0;
        }

        // Don't spoil items the player has neither researched nor ever seen. Ungated
        // items have no prerequisites and always pass.
        //
        // Owning one is enough on its own: looted, traded and starting gear never went
        // through research, and hiding an item the player can see in their inventory
        // would leave them unable to write a policy for it. ObjCountByDef counts every
        // player-owned item whatever state it is in, equipped included, so a slot whose
        // items are all currently worn still offers them.
        private static bool IsVisible(PolicyTabContext ctx, InventoryItemDef item) {
            if (item.Def == null) { return false; }
            return ctx.S.Research.IsDefUnlocked(item.Def)
                || ctx.S.Query.ObjCountByDef.Ask(item.Def) > 0;
        }

        private static int CompareItems(InventoryItemDef a, InventoryItemDef b) {
            return string.Compare(a.ItemName, b.ItemName, StringComparison.Ordinal);
        }
    }
}
