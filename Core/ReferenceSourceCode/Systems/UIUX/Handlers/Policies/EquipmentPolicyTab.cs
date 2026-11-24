using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Systems.Items;
using Game.Systems.Policies;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class EquipmentPolicyTab : BasePolicyTab {
        [SelfInit]
        private static void Register() {
            Register(new EquipmentPolicyTab());
        }

        public const string TabId = "Equipment";
        public override string Id => TabId;
        public override string NameT => T.Equipment;
        public override string Icon => IconId.CClothing;
        public override int Order => 0;

        // Everything below is per-Build state: Build is the only entry point and resets
        // all of it, and the tab is a single registered instance.
        private PolicyTabContext ctx;
        private List<InventorySlotType> slotTypes;
        private int footerIdx;
        private bool resetShown;
        private bool pasteShown;

        public override void Build(PolicyTabContext ctx) {
            this.ctx = ctx;
            slotTypes = SlotTypesFor(ctx);
            footerIdx = 0;
            resetShown = false;
            pasteShown = false;
            int groupIdx = ctx.StartGroupIdx;
            BuildOriginBlock(ref groupIdx);
            // A being or species that inherits has no slots of its own to configure -
            // showing them would invite edits that silently promote the scope, and the
            // origin dropdown above is the way in. The global scope always shows them:
            // it is what everything else inherits from.
            bool showsSlots = ctx.Scope == PolicyScope.Global
                || EquipmentPolicyScope.OwnsPolicy(ctx);
            if (showsSlots) {
                BuildSlotBlock(ref groupIdx);
            }
            BuildFooter(showsSlots);
        }

        // The global policy just came into existence, so the footer had nothing to
        // reset when the tab was built. Being and species scopes only show their slots
        // once they already own a policy, so nothing else here can promote a scope.
        private void OnPromoted() {
            if (ctx.Life.IsDisposed) { return; }
            if (ctx.Scope != PolicyScope.Global) { return; }
            if (resetShown) { return; }
            SpawnResetButton();
        }

        // --- Origin ---

        private void BuildOriginBlock(ref int groupIdx) {
            // ctx is per-build state, so callbacks capture the instance they were built
            // with rather than reading whatever the field holds when they fire.
            var ctx = this.ctx;
            if (ctx.Scope == PolicyScope.Global) {
                // var header = UISpawn.Widget<WText>(ctx.Life, UIPrefabId.WContent2Header,
                //     block.Container, idx++);
                // header.Load(T.AdjDefault);
                // var row = UISpawn.Widget<WIconText>(ctx.Life, UIPrefabId.WContent2Text,
                //     block.Container, idx++);
                // row.Load(IconId.CTargetGlobal, T.AdjDefault);
                return;
            }
            var block = UISpawn.Block(ctx.Life, ctx.Content, groupIdx++);
            int idx = 0;
            var w = UISpawn.Widget<WDropdownWithIcon>(ctx.Life, UIPrefabId.WContent2Dropdown,
                block.Container, idx++);
            bool hasOwn;
            var options = new List<string>();
            if (ctx.Scope == PolicyScope.Being) {
                hasOwn = ctx.Being?.Mind?.EquipmentPolicy != null;
                w.Icon.Load(IconId.CPersona);
                options.Add($"{T.PolicyActive}: {T.PolicyOriginIndividual}");
                options.Add($"{T.PolicyActive}: "
                    + $"{EquipmentPolicyTexts.InheritedName(ctx.Sys, ctx.Being)}");
            } else {
                hasOwn = ctx.Sys.SpeciesEquipment(ctx.Species) != null;
                w.Icon.Load(IconId.CDNA);
                options.Add($"{T.PolicyActive}: {T.Species}");
                options.Add($"{T.PolicyActive}: {T.AdjDefault}");
            }
            w.Dropdown.Load(options, hasOwn ? 0 : 1, sel => OnOriginSelected(ctx, sel));
        }

        private static void OnOriginSelected(PolicyTabContext ctx, int selected) {
            bool wantsOwn = selected == 0;
            if (ctx.Scope == PolicyScope.Being) {
                if (ctx.Being?.Mind == null) { return; }
                if (wantsOwn == (ctx.Being.Mind.EquipmentPolicy != null)) { return; }
                ctx.Sys.SetIndividualEquipment(ctx.Being,
                    wantsOwn ? ctx.Sys.InheritedFor(ctx.Being).Clone() : null);
            } else {
                if (ctx.Species == null) { return; }
                if (wantsOwn == (ctx.Sys.SpeciesEquipment(ctx.Species) != null)) { return; }
                ctx.Sys.SetSpeciesEquipment(ctx.Species,
                    wantsOwn ? ctx.Sys.InheritedForSpecies().Clone() : null);
            }
            // Every slot in the tab now reads from a different policy.
            ctx.ApplyAndRebuild();
        }

        // --- Slots ---

        private void BuildSlotBlock(ref int groupIdx) {
            if (slotTypes.Count == 0) { return; }
            // All slots share one block, which keeps them visually separate from
            // the active-policy dropdown above.
            var slotBlock = UISpawn.Block(ctx.Life, ctx.Content, groupIdx++);
            int slotIdx = 0;
            foreach (var type in slotTypes) {
                new EquipmentPolicySlotSection(ctx, type, OnPromoted)
                    .Spawn(slotBlock.Container, slotIdx++);
            }
        }

        private static List<InventorySlotType> SlotTypesFor(PolicyTabContext ctx) {
            var result = new List<InventorySlotType>();
            if (ctx.Scope == PolicyScope.Being && ctx.Being?.Inventory != null) {
                foreach (var slot in ctx.Being.Inventory.Equipment.Values) {
                    result.Add(slot.Type);
                }
            } else if (ctx.Scope == PolicyScope.Species && ctx.Species != null) {
                var ids = ctx.Species.InventorySlotTypes;
                if (ids != null) {
                    foreach (var id in ids) {
                        if (InventorySlotType.TryGet(id, ctx.Species, out var type)) {
                            result.Add(type);
                        }
                    }
                }
            } else {
                foreach (var type in InventorySlotType.All) {
                    result.Add(type);
                }
            }
            result.Sort(CompareSlotTypes);
            return result;
        }

        private static int CompareSlotTypes(InventorySlotType a, InventorySlotType b) {
            int cmp = a.Order.CompareTo(b.Order);
            if (cmp != 0) { return cmp; }
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }

        // --- Footer ---

        // The footer fits two buttons. Copy comes first wherever the slot rows are
        // visible - the player copies what they see. The global scope has Reset in the
        // second place, so it can be copied from but not pasted into.
        private void BuildFooter(bool showsSlots) {
            if (showsSlots && slotTypes.Count > 0) {
                SpawnCopyButton();
            }
            if (ctx.Scope == PolicyScope.Global) {
                // Species and being scopes reset by switching their origin dropdown back
                // to the inherited option, so a second control there would be redundant.
                if (ctx.Sys.GlobalEquipment != null) {
                    SpawnResetButton();
                }
            } else if (EquipmentPolicyClipboard.CanPasteInto(slotTypes)) {
                SpawnPasteButton();
            }
        }

        private WButtonText SpawnFooterButton() {
            var btn = UISpawn.Widget<WButtonText>(ctx.Life,
                UIPrefabId.WFooterItem_ConfirmButton, ctx.Footer.Container, footerIdx++);
            ctx.Footer.SetActiveFast(true);
            return btn;
        }

        private void SpawnCopyButton() {
            var ctx = this.ctx;
            var slotTypes = this.slotTypes;
            var btn = SpawnFooterButton();
            btn.Load(T.VerbCopy, () => {
                EquipmentPolicyClipboard.Copy(ctx, slotTypes);
                UISounds.PlayShowDialog();
                // The first copy is what makes Paste available on this very scope.
                if (ctx.Scope != PolicyScope.Global && !pasteShown
                        && EquipmentPolicyClipboard.CanPasteInto(slotTypes)) {
                    SpawnPasteButton();
                }
            });
        }

        private void SpawnPasteButton() {
            var ctx = this.ctx;
            var slotTypes = this.slotTypes;
            var btn = SpawnFooterButton();
            btn.Load(T.VerbPaste, () => {
                if (!EquipmentPolicyClipboard.PasteInto(ctx, slotTypes)) { return; }
                UISounds.PlayShowDialog();
                // Every slot changes and the origin may flip to Individual.
                ctx.ApplyAndRebuild();
            });
            UIUtils.AddTipFunc(ctx.Life, btn, () => EquipmentPolicyClipboard.PasteTip(slotTypes));
            pasteShown = true;
        }

        private void SpawnResetButton() {
            var ctx = this.ctx;
            var btn = SpawnFooterButton();
            btn.Load(T.ResetToDefaults, () => {
                ctx.Sys.SetGlobalEquipment(null);
                ctx.ApplyAndRebuild();
            });
            resetShown = true;
        }
    }
}
