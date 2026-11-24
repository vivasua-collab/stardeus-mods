using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    // One inventory slot's collapsible section on the Equipment policy tab. Owns the
    // lifetime of its own rows, so an edit that changes which rows exist respawns just
    // this section instead of the whole tab - which would throw away the player's
    // scroll position and every other section's widgets.
    public sealed class EquipmentPolicySlotSection {
        // Which slot sections are open, by slot type hash. Kept static so expansion
        // survives the tab rebuilds that an origin change still causes.
        private static readonly HashSet<int> expandedSlots = new();

        [ExecuteOnReload]
        private static void CleanupStatics() {
            expandedSlots.Clear();
        }

        private readonly PolicyTabContext ctx;
        private readonly InventorySlotType type;
        private readonly Action onPromoted;
        private readonly bool isWeapon;

        private WContentListButton header;
        private Lifetime rowsLife;

        public EquipmentPolicySlotSection(PolicyTabContext ctx, InventorySlotType type,
                Action onPromoted) {
            this.ctx = ctx;
            this.type = type;
            this.onPromoted = onPromoted;
            isWeapon = type.IdH == InventorySlotIdH.Weapon;
        }

        public void Spawn(RectTransform container, int siblingIdx) {
            header = UISpawn.Widget<WContentListButton>(ctx.Life,
                UIPrefabId.WContent2ListButton, container, siblingIdx);
            header.Load(EquipmentPolicyTexts.SlotIcon(type), type.Name, Summary());
            header.LoadOnToggle(ctx.Life, OnToggle, expandedSlots.Contains(type.IdH));
        }

        private bool OnToggle(Lifetime life, RectTransform container, bool willShow,
                bool wasLoaded) {
            if (!willShow) {
                expandedSlots.Remove(type.IdH);
                return wasLoaded;
            }
            expandedSlots.Add(type.IdH);
            if (wasLoaded) { return true; }
            SpawnRows(container);
            return true;
        }

        // This section's rows changed shape - a row appeared or disappeared. Respawns
        // them and nothing else, so the rest of the tab and the scroll position hold.
        // Deferred a frame because these come from dropdown callbacks, and WDropdown
        // touches its own widget again after invoking us - tearing it down mid-callback
        // is not safe.
        private void RebuildRows() {
            ctx.ApplyCommit();
            Coroutines.OnNextFrame(() => {
                // The whole tab may have been rebuilt in the meantime, which despawns
                // the header back into the pool.
                if (ctx.Life.IsDisposed) { return; }
                header.RebuildContainer();
                RefreshHeader();
                ctx.RelayoutContent();
            });
        }

        // A discrete edit that leaves the rows as they are: only the collapsed summary
        // can differ.
        private void CommitRowEdit() {
            ctx.ApplyCommit();
            RefreshHeader();
        }

        private void RefreshHeader() {
            header.SetText2(Summary());
        }

        private string Summary() {
            return EquipmentPolicyTexts.Summary(Effective(), type);
        }

        private EquipmentSlotPolicy Effective() {
            return EquipmentPolicyScope.Effective(ctx, type);
        }

        private EquipmentSlotPolicy Editable() {
            return EquipmentPolicyScope.Editable(ctx, type, onPromoted);
        }

        // --- Rows ---

        private void SpawnRows(RectTransform container) {
            rowsLife?.Dispose();
            rowsLife = ctx.Life.CreateChild($"Slot_{type.Id}");
            var life = rowsLife;
            var policy = Effective();
            int idx = 0;

            idx = SpawnModeRow(life, container, policy, idx);
            idx = EquipmentPolicyWarnings.Spawn(ctx, life, container, type, policy, idx);

            if (policy.Mode == AutoEquipMode.Manual) {
                // Nothing else gates a slot nobody auto-fills, except what happens to
                // a weapon that gets dropped, which is independent of the mode.
                if (isWeapon) {
                    SpawnForbidRow(life, container, policy, idx);
                }
                return;
            }

            if (isWeapon) {
                idx = SpawnWeaponKindRow(life, container, policy, idx);
            }
            idx = SpawnItemRow(life, container, policy, idx);
            idx = SpawnColorRow(life, container, policy, idx);
            idx = SpawnQualityRows(life, container, policy, idx);
            idx = SpawnIncompatibleRow(life, container, policy, idx);
            idx = SpawnOwnershipRows(life, container, policy, idx);
            if (isWeapon) {
                SpawnForbidRow(life, container, policy, idx);
            }
        }

        private int SpawnModeRow(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            var w = UISpawn.Widget<WDropdownWithIcon>(life, UIPrefabId.WContent2Dropdown,
                container, idx++);
            w.Icon.Load(IconId.CTask);
            // Option order must match AutoEquipMode's values.
            var options = new List<string> {
                Labelled(T.PolicyAutoEquip, EquipmentPolicyTexts.ModeName(AutoEquipMode.Manual)),
                Labelled(T.PolicyAutoEquip, EquipmentPolicyTexts.ModeName(AutoEquipMode.IfEmpty)),
                Labelled(T.PolicyAutoEquip, EquipmentPolicyTexts.ModeName(AutoEquipMode.Auto)),
            };
            w.Dropdown.Load(options, (int) policy.Mode, sel => {
                // Manual hides everything below it, and the other modes bring it back.
                Editable().Mode = (AutoEquipMode) sel;
                RebuildRows();
            });
            return idx;
        }

        private int SpawnWeaponKindRow(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            var w = UISpawn.Widget<WDropdownWithIcon>(life, UIPrefabId.WContent2Dropdown,
                container, idx++);
            w.Icon.Load(IconId.CCombat);
            // Option order must match WeaponKind's values.
            var options = new List<string> {
                Labelled(T.NounWeaponType, EquipmentPolicyTexts.WeaponKindName(WeaponKind.Any)),
                Labelled(T.NounWeaponType, EquipmentPolicyTexts.WeaponKindName(WeaponKind.Melee)),
                Labelled(T.NounWeaponType, EquipmentPolicyTexts.WeaponKindName(WeaponKind.Ranged)),
            };
            w.Dropdown.Load(options, (int) policy.WeaponKind, sel => {
                // Decides whether the incapable-being warning row applies.
                Editable().WeaponKind = (WeaponKind) sel;
                RebuildRows();
            });
            return idx;
        }

        private int SpawnItemRow(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            var w = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                container, idx++);
            w.Load(ItemIcon(policy.ItemDefId), T.PolicyItem);
            w.SetText2(EquipmentPolicyTexts.ItemName(policy.ItemDefId));
            // Effective is resolved at click time, not captured here: the scope may have
            // been promoted since, which replaces the policy instance this row was
            // built from.
            w.SetOnClick(() => EquipmentPolicyPickers.ShowItemPicker(ctx, w, type,
                // Pinning an item narrows - or removes - the colour choice below.
                Effective(), Editable, RebuildRows));
            return idx;
        }

        private int SpawnColorRow(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            // Slots whose items have no colour group have nothing to choose from.
            if (!EquipmentPolicyPickers.HasColorChoice(ctx, type, policy.ItemDefId)) {
                return idx;
            }
            var w = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                container, idx++);
            LoadColorRow(w, policy);
            w.SetOnClick(() => EquipmentPolicyPickers.ShowColorPicker(ctx, w, type,
                Effective(), Editable, () => {
                    // Which colours exist depends on the item, not on the colour, so
                    // the row set is the same as before - refresh this row in place.
                    LoadColorRow(w, Effective());
                    CommitRowEdit();
                }));
            return idx;
        }

        private static void LoadColorRow(WButtonIconText2 w, EquipmentSlotPolicy policy) {
            var color = Consts.ColorWhite;
            bool hasColor = false;
            if (policy.ColorId != null) {
                hasColor = Palette.TryColorById(policy.ColorId, out color);
            }
            // A tinted swatch when a colour is pinned, the colour wheel for "Any" -
            // an untinted swatch just looks like a blank white box. Load resets the
            // tint, so going back to "Any" clears a previously pinned swatch.
            w.Load(hasColor ? IconId.CColorPreview : IconId.CColorWheel, T.NounColor);
            w.SetText2(EquipmentPolicyTexts.ColorName(policy.ColorId));
            if (hasColor) {
                w.SetTint(color);
            }
        }

        // Two separate labelled sliders rather than one stacked range control, to
        // match the quality filter on storages (see ObjStorageTab.LoadFilterBlock).
        private int SpawnQualityRows(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            var minQ = UISpawn.Widget<WSliderWithIconText2>(life,
                UIPrefabId.WContent2Text2Slider, container, idx++);
            minQ.Item.Load(IconId.CPercentRed, T.MinQuality, null);
            var maxQ = UISpawn.Widget<WSliderWithIconText2>(life,
                UIPrefabId.WContent2Text2Slider, container, idx++);
            maxQ.Item.Load(IconId.CPercentGreen, T.MaxQuality, null);

            minQ.Slider.Load(policy.QualityMin, v => {
                var editable = Editable();
                editable.QualityMin = v;
                if (editable.EffectiveQualityMax < v) {
                    // Keep the range valid rather than letting it exclude everything.
                    editable.QualityMax = v;
                    maxQ.Slider.SetValue(v, notify: false);
                }
                OnQualityDragged();
            });
            minQ.Slider.SetValueTextFunc(v => Units.Percentage(v));

            maxQ.Slider.Load(policy.EffectiveQualityMax, v => {
                var editable = Editable();
                editable.QualityMax = v;
                if (editable.QualityMin > v) {
                    editable.QualityMin = v;
                    minQ.Slider.SetValue(v, notify: false);
                }
                OnQualityDragged();
            });
            maxQ.Slider.SetValueTextFunc(QualityMaxText);
            return idx;
        }

        private void OnQualityDragged() {
            RefreshHeader();
            ctx.ApplyLive();
        }

        // Matches how storage filters render an open-ended maximum.
        private const string UnboundedQualityText = "∞";

        private static string QualityMaxText(float v) {
            return v >= EquipmentSlotPolicy.UnboundedQuality
                ? UnboundedQualityText
                : Units.Percentage(v);
        }

        private int SpawnIncompatibleRow(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            var w = UISpawn.Widget<WDropdownWithIcon>(life, UIPrefabId.WContent2Dropdown,
                container, idx++);
            w.Icon.Load(IconId.CRecycling);
            // Option order must match IncompatibleAction's values.
            var options = new List<string> {
                Labelled(T.PolicyIncompatible,
                    EquipmentPolicyTexts.IncompatibleName(IncompatibleAction.KeepUntilReplaced)),
                Labelled(T.PolicyIncompatible,
                    EquipmentPolicyTexts.IncompatibleName(IncompatibleAction.UnequipNow)),
            };
            w.Dropdown.Load(options, (int) policy.Incompatible, sel => {
                Editable().Incompatible = (IncompatibleAction) sel;
                CommitRowEdit();
            });
            return idx;
        }

        // Only worn gear carries ownership history, so a weapon slot gets no rows at
        // all. Being inert for beings that cannot care (robots) is a separate rule,
        // enforced by the filter rather than here: one species or global scope covers
        // biologicals and machines at once.
        private int SpawnOwnershipRows(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            if (!type.Worn) { return idx; }
            var used = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            used.Item.Load(IconId.CRecycling, T.PolicyForbidSecondHand,
                policy.ForbidSecondHand ? T.Yes : T.No);
            used.Toggle.Load(policy.ForbidSecondHand, on => {
                Editable().ForbidSecondHand = on;
                used.Item.SetText2(on ? T.Yes : T.No);
                CommitRowEdit();
            });
            UIUtils.AddTip(life, used, T.PolicyForbidSecondHandTip);

            var dead = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            dead.Item.Load(IconId.CSkull, T.PolicyForbidDeadMans,
                policy.ForbidDeadMans ? T.Yes : T.No);
            dead.Toggle.Load(policy.ForbidDeadMans, on => {
                Editable().ForbidDeadMans = on;
                dead.Item.SetText2(on ? T.Yes : T.No);
                CommitRowEdit();
            });
            UIUtils.AddTip(life, dead, T.PolicyForbidDeadMansTip);
            return idx;
        }

        private int SpawnForbidRow(Lifetime life, RectTransform container,
                EquipmentSlotPolicy policy, int idx) {
            var w = UISpawn.Widget<WToggleWithIconText2>(life,
                UIPrefabId.WContent2Text2Toggle, container, idx++);
            w.Item.Load(IconId.CForbid, T.PolicyForbidDropped,
                policy.ForbidWhenDropped ? T.Yes : T.No);
            w.Toggle.Load(policy.ForbidWhenDropped, on => {
                Editable().ForbidWhenDropped = on;
                w.Item.SetText2(on ? T.Yes : T.No);
                ctx.ApplyCommit();
            });
            UIUtils.AddTip(life, w, T.PolicyForbidDroppedTip);
            return idx;
        }

        private string ItemIcon(string itemDefId) {
            if (itemDefId != null) {
                var item = InventoryItemDef.FastGet(Hashes.S(itemDefId));
                if (item?.Def != null) { return item.Def.Preview; }
            }
            return EquipmentPolicyTexts.SlotIcon(type);
        }

        private static string Labelled(string label, string value) {
            return $"{label}: {value}";
        }
    }
}
