using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Research;
using Game.Systems.BodyParts;
using Game.Systems.Crafting;
using Game.Systems.Items;
using KL.Utils;

namespace Game.Systems.UIUX {
    public sealed class InventoryPanelHandler : BaseWidgetHandler<InventoryPanelWidget> {
        private readonly Dictionary<MatType, InvMatBlock> matBlocks = new();
        private readonly Dictionary<InventoryItemDef, InvEquipmentBlock> equipmentBlocks = new();
        private readonly Dictionary<BodyPartDef, InvBodyPartBlock> bodyPartBlocks = new();
        public InventoryPanelHandler(GameState s, InventoryPanelWidget widget) : base(s, widget) {
            s.Sig.MatCountChanged.AddListener(OnMatCountChanged);
            s.Sig.ObjCountChanged.AddListener(OnObjCountChanged);
            s.UIQuery.ToggleInventoryPanel.SetResponder(TogglePanel);
            s.Sig.SetFavMat.AddListener(OnSetFavMat);
            s.Sig.ResearchChanged.AddListener(OnResearchChanged);

            widget.Initialize(s, T.OverlayTipResources);

            widget.LoadMats(matBlocks);
            widget.LoadEquipment(equipmentBlocks);
            widget.LoadBodyParts(bodyPartBlocks);
        }

        private void OnSetFavMat(MatType mat, bool yes, int threshold) {
            if (matBlocks.TryGetValue(mat, out var block)) {
                block.UpdateFavState(yes, threshold);
            }
        }

        private bool TogglePanel() {
            if (!widget.IsShowing) {
                SyncMatBlocks();
            }
            widget.Toggle();
            return widget.IsShowing;
        }

        private void OnResearchChanged(Def def) {
            if (widget.IsShowing && SyncMatBlocks()) {
                widget.RebuildMats();
            }
        }

        // Discovered mats we own none of are absent from the count cache, so nothing
        // creates a row for them after a reload. Seed those rows here at 0, and they
        // stay listed once the counts run out again.
        // Materials that research made craftable are listed at 0 too, before the first one
        // is made: the player is waiting for them (Alien Datoids, STA-1773). Recipes that
        // need no research stay out, or the list would show every craftable from day one.
        // They are rows only: KnownMaterials (owned at least once) is read by story events.
        private bool SyncMatBlocks() {
            bool added = false;
            foreach (var mat in S.Sys.Inventory.KnownMaterials) {
                added |= TryAddEmptyMatBlock(mat);
            }
            var research = S.Research;
            foreach (var craftable in Craftable.All) {
                var def = craftable.ProductDef;
                if (def == null || Researchable.IsEmptyPrereq(def)
                        || !research.IsDefActuallyUnlocked(def)) {
                    continue;
                }
                var mat = MatType.FindByDef(def);
                if (mat != null) {
                    added |= TryAddEmptyMatBlock(mat);
                }
            }
            return added;
        }

        private bool TryAddEmptyMatBlock(MatType mat) {
            if (matBlocks.ContainsKey(mat)) { return false; }
            var block = new InvMatBlock(S, mat);
            matBlocks[mat] = block;
            block.SetCount(0);
            return true;
        }

        private void OnObjCountChanged(Dictionary<Def, int> counts, Dictionary<Def, int> equippedCounts) {
            bool needsEquipmentRebuild = false;
            bool needsBodyPartsRebuild = false;
            foreach (var kv in counts) {
                var def = kv.Key;
                var idH = def.IdH;
                var cnt = kv.Value;
                if (InventoryItemDef.TryForDef(idH, out var itemDef)) {
                    if (!equipmentBlocks.TryGetValue(itemDef, out var item)) {
                        item = new InvEquipmentBlock(itemDef);
                        equipmentBlocks[itemDef] = item;
                        needsEquipmentRebuild = true;
                    }
                    equippedCounts.TryGetValue(def, out var eqCnt);
                    item.SetCount(cnt, eqCnt);
                } else if (BodyPartDef.TryForDef(idH, out var partDef)) {
                    if (!bodyPartBlocks.TryGetValue(partDef, out var item)) {
                        item = new InvBodyPartBlock(partDef);
                        bodyPartBlocks[partDef] = item;
                        needsBodyPartsRebuild = true;
                    }
                    item.SetCount(cnt);
                }
            }
            if (needsEquipmentRebuild) {
                widget.RebuildEquipment();
            }
            if (needsBodyPartsRebuild) {
                widget.RebuildBodyParts();
            }
        }

        private void OnMatCountChanged(Dictionary<MatType, int> counts) {
            bool needsRebuild = false;
            foreach (var mc in counts) {
                var mat = mc.Key;
                var cnt = mc.Value;
                if (!matBlocks.TryGetValue(mat, out var block)) {
                    S.Sys.Inventory.RegisterKnownMaterial(mat);
                    block = new InvMatBlock(S, mat);
                    matBlocks[mat] = block;
                    needsRebuild = true;
                }
                block.SetCount(cnt);
            }
            foreach (var mb in matBlocks) {
                if (!counts.ContainsKey(mb.Key)) {
                    mb.Value.SetCount(0);
                }
            }
            if (needsRebuild) {
                widget.RebuildMats();
            }
        }
    }
}
