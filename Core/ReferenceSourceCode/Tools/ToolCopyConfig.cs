using System;
using Game.Constants;
using Game.Data;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Components;
using Game.Rendering;
using Game.UI;
using KL.Utils;
using Game.Input;
using KL.Collections;
using Game.Systems.UIUX;
namespace Game.Tools {
    public sealed class ToolCopyConfig : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolCopyConfig());
        }
        public override bool SkipMenu => true;
        public override string MenuIcon => IconId.WConfig;
        public override ActionType Shortcut => ActionType.ShortcutCopyPaste;
        public override string[] ShortcutKeys => new[] { "<Keyboard>/v" };
        public override bool ShortcutCyclesParameters => false;
        public override string ShortcutNextParam =>
            currentParam == "copy" ? "paste" : "copy";

        public const string ToolId = "copy_config";
        public override string Id => ToolId;

        public override bool IsConfigurable => false;
        private readonly List<string> availableParams = new() {
            "copy",
            "paste",
        };
        public override int Cursor {
            get {
                switch (currentParam) {
                    case "copy": return CursorTypeH.CursorCog;
                    case "paste": return CursorTypeH.CursorCogs;
                    default: return CursorTypeH.Cursor;
                }
            }
        }
        private Tile hoveredTile;
        private readonly List<Being> hoveredBeings = new();
        private readonly List<ICopyableComp> hoveredCopyable = new();
        private ICopyableComp activeCopyable;
        private ICopyableComp activeCopyableMemory;

        private string currentParam;

        protected override void Initialize() {
            showDragRectSize = false;
        }

        public static void SwitchToCopy(ICopyableComp cp) {
            if (cp.Entity.S.Toolbox.ActiveTool is ToolCopyConfig activeTool) {
                activeTool.StartCopying(cp);
                return;
            }
            cp.Entity.S.Sig.ShortcutPressed.Send(ActionType.ShortcutCopyPaste);
            if (cp.Entity.S.Toolbox.ActiveTool is ToolCopyConfig cop) {
                cop.StartCopying(cp);
            }
        }

        public static void SwitchToCopy(IReadOnlyList<ICopyableComp> copyables) {
            if (copyables.Count < 2) {
                SwitchToCopy(copyables[0]);
                return;
            }
            var qc = QuickChoice.Create(T.CopyConfig);
            for (int i = 0; i < copyables.Count; i++) {
                var comp = copyables[i];
                qc.AddButton(comp.CopyConfigText, () => {
                    SwitchToCopy(comp);
                });
            }
            A.S.UIQuery.ShowQuickChoice.Ask(qc);
        }

        public void SetMode(string param) {
            if (activeCopyableMemory != null) {
                activeCopyable = activeCopyableMemory;
                activeCopyableMemory = null;
            }
            if (activeCopyable != null && activeCopyable.Entity.IsActive) {
                // D.Log("Showing link in configure");
                EntityUtils.ShowEntityLink(null, activeCopyable.Entity, true);
            } else {
                activeCopyable = null;
            }
            currentParam = param;
        }
        private readonly List<ICopyableComp> currentCopyables = new();
        private readonly List<ICopyableComp> compatibleCopyables = new();
        public override void OnHover(Vector2 coords) {
            isHoverHidden = false;
            if (isDragging) {
                OnHoverDrag();
                ShowCopyableTip();
                return;
            }
            var pos = EntityUtils.ToPosSafe(coords);
            var posChanged = false;
            if (pos != lastHoverPos) {
                posChanged = true;
                hoveredTile = EntityUtils.GetTopmostParentTile(pos);
                lastHoverPos = pos;
                hoveredCopyable.Clear();
                currentCopyables.Clear();
                hoveredBeings.Clear();
                if (S.Beings.ByPos.TryGetValue(pos, out var beings))  {
                    hoveredBeings.AddRange(beings);
                }
            }
            if (activeCopyable == null) {
                currentCopyables.Clear();
                if (HasCopyableComp(hoveredBeings) || HasCopyableComp(hoveredTile)) {
                    hoveredCopyable.Clear();
                    hoveredCopyable.AddRange(currentCopyables);
                    ShowGreenTile(pos);
                } else {
                    hoveredCopyable.Clear();
                    ShowRedTile(pos);
                }
            } else {
                compatibleCopyables.Clear();
                if (activeCopyable.Entity is Tile) {
                    if (HasCopyableComp(activeCopyable, hoveredTile)) {
                        ShowGreenTile(pos);
                        compatibleCopyables.AddRange(currentCopyables);
                    }
                } else if (activeCopyable.Entity is Being) {
                    if (HasCopyableComp(hoveredBeings)) {
                        ShowGreenTile(pos);
                        compatibleCopyables.AddRange(currentCopyables);
                    }
                }
                hoveredCopyable.Clear();
                if (compatibleCopyables.Count > 0) {
                    hoveredCopyable.AddRange(compatibleCopyables);
                    ShowGreenTile(pos);
                } else {
                    ShowRedTile(pos);
                }
            }
            if (posChanged) {
                ShowCopyableTip();
            }
        }

        private void ShowCopyableTip() {
            if (UIShowing.MouseTooltip) { return; }
            var key = The.Bindings.GetControlName(ActionType.Click);
            var sb = Caches.StringBuilder4;
            sb.Clear();
            List<ICopyableComp> cops;
            if (!Arrays.IsEmpty(compatibleCopyables)) {
                cops = compatibleCopyables;
            } else {
                cops = hoveredCopyable;
            }
            if (!Arrays.IsEmpty(cops)) {
                var nc = Caches.NamedCounts;
                nc.Clear();
                foreach (var comp in cops) {
                    nc.GetIncrement(comp.Entity.Definition.CodexLink);
                }
                sb.Append(Texts.SubTitle(T.HoveringCompatibleObject));
                foreach (var name in nc) {
                    if (name.Value > 1) {
                        sb.AppendFormat("{0} x{1}\n", name.Key, name.Value);
                    } else {
                        sb.AppendFormat("{0}\n", name.Key);
                    }
                }
                nc.Clear();
                sb.AppendLine();
            }

            if (activeCopyable == null) {
                sb.Append(T.CopyConfigCopyTip_V1.F(key));
            } else {
                sb.Append(Texts.SubTitle(T.CopyConfigCopied));
                sb.AppendFormat("{0} ({1})\n", activeCopyable.Entity.Definition.NameT,
                    Texts.Accented(activeCopyable.CopyConfigText, false));
                sb.AppendLine();
                sb.Append(T.CopyConfigPasteTip_V1.F(key));
            }
            The.SysSig.ShowTooltip.Send(sb.ToString(), false);
        }

        private bool HasCopyableComp(ICopyableComp target, List<Being> bs) {
            if (Arrays.IsEmpty(bs)) { return false; }
            bool hasComps = false;
            for (int i = bs.Count - 1; i >= 0; i--) {
                hasComps |= HasCopyableComp(target, bs[i]);
            }
            return hasComps;
        }
        private bool HasCopyableComp(ICopyableComp target, Being b) {
            if (b == null) { return false; }
            if (b.IsDead || !b.IsActive) { return false; }
            bool hasComps = false;
            for (int i = b.Components.Length - 1; i >= 0; i--) {
                IComponent c = b.Components[i];
                if (c is ICopyableComp co && co.IsCopyable && target.CanCopyTo(co)) {
                    hasComps = true;
                    currentCopyables.Add(co);
                }
            }
            return hasComps;
        }
        private bool HasCopyableComp(ICopyableComp target, Tile tile) {
            if (tile == null) { return false; }
            bool hasComps = false;
            if (tile?.IsConstructed == true && (tile.ENode?.IsReachable != false)) {
                for (int i = 0; i < tile.Components.Length; i++) {
                    IComponent c = tile.Components[i];
                    if (c is ICopyableComp co && co.IsCopyable && target.CanCopyTo(co)) {
                        hasComps = true;
                        currentCopyables.AddUniqueInefficient(co);
                    }
                }
            }
            return hasComps;
        }

        private bool HasCopyableComp(List<Being> beings) {
            if (Arrays.IsEmpty(beings)) { return false; }
            bool hasComps = false;
            for (int i1 = 0; i1 < beings.Count; i1++) {
                Being b = beings[i1];
                if (b.IsDead || !b.IsActive) { continue; }
                for (int i = b.Components.Length - 1; i >= 0; i--) {
                    IComponent c = b.Components[i];
                    if (c is ICopyableComp co && co.IsCopyable) {
                        hasComps = true;
                        currentCopyables.Add(co);
                    }
                }
            }
            return hasComps;
        }

        private bool HasCopyableComp(Tile tile) {
            if (tile == null) { return false; }
            bool hasComps = false;
            if (tile?.IsConstructed == true && (tile.ENode?.IsReachable != false)) {
                for (int i = 0; i < tile.Components.Length; i++) {
                    IComponent c = tile.Components[i];
                    if (c is ICopyableComp co && co.IsCopyable) {
                        hasComps = true;
                        currentCopyables.AddUniqueInefficient(co);
                    }
                }
            }
            return hasComps;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            isDragging = true;
            var rect = DragRect(start, end);
            // FIXME
            if (currentParam == "copy") {
                //HandleDragRect(rect, Consts.GreenRectColor);
                ClearPreview();
            } else {
                HandleDragRect(rect, Consts.GreenRectColor);
            }
        }

        public override bool Cancel() {
            if (activeCopyable != null) {
                // D.Log("Hiding link in cancel");
                EntityUtils.HideEntityLink();
                activeCopyable = null;
            }
            The.SysSig.HideTooltip.Send();
            currentParam = null;
            currentCopyables.Clear();
            hoveredCopyable.Clear();
            compatibleCopyables.Clear();
            hoveredBeings.Clear();
            return false;
        }

        public override void Activate() {
            currentParam = "copy";
            base.Activate();
        }

        public override void Deactivate() {
            Cancel();
            base.Deactivate();
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (currentParam == "copy") {
                if (!isDragging && hoveredCopyable.Count > 0) {
                    SwitchToCopy(hoveredCopyable);
                } else {
                    if (activeCopyable != null) {
                    // D.Log("Hiding link in onclickup");
                        EntityUtils.HideEntityLink();
                    }
                    activeCopyable = null;
                }
            } else if (currentParam == "paste" && activeCopyable != null) {
                bool copied = false;
                if (isDragging) {
                    if (activeCopyable.Entity is Being) {
                        S.Beings.FindVisualInRect(startPos, position, Caches.BeingList,
                                b => b.IsAlive);
                        currentCopyables.Clear();
                        HasCopyableComp(activeCopyable, Caches.BeingList);
                    }
                    for (int i = dragPositions.Count - 1; i >= 0; i--) {
                        int pos = dragPositions[i];
                        foreach (var id in RenderLayers.TileLayerIds) {
                            var tile = S.Grids[id].Get(pos);
                            tile = EntityUtils.ParentObj(tile);
                            HasCopyableComp(activeCopyable, tile);
                        }
                    }
                } else {
                    if (hoveredCopyable.Count > 0) {
                        currentCopyables.Clear();
                        foreach (var cop in hoveredCopyable) {
                            if (activeCopyable.CanCopyTo(cop)) {
                                currentCopyables.Add(cop);
                            }
                        }
                    }
                }
                if (currentCopyables.Count > 0) {
                    copied = CopyToCopyable(currentCopyables);
                }
                if (copied) {
                    UISounds.PlayShowDialog();
                } else {
                    UISounds.PlayActionDenied();
                }
            }
            currentCopyables.Clear();
            ClearDragPositions();
            isDragging = false;
        }

        private bool CopyToCopyable(List<ICopyableComp> comps) {
            bool copied = false;
            for (int i1 = 0; i1 < comps.Count; i1++) {
                ICopyableComp cp = comps[i1];
                if (activeCopyable.CopyConfigTo(cp)) {
                    copied = true;
                }
            }
            return copied;
        }

        public override string ToString() {
            return Id;
        }


        public void StartCopying(ICopyableComp cp) {
            activeCopyable = cp;
            currentCopyables.Clear();
            UISounds.PlayDigitalConfirm();
            EntityUtils.ShowEntityLink(null,
                activeCopyable.Entity, true);
            activeCopyableMemory = cp;
            currentParam = "paste";
            The.SysSig.SetCursor.Send(Cursor);
        }

        public static bool TryStartCopying() {
            var selected = A.S.Query.GetSelectedParentTile.Ask(true);
            if (selected != null) {
                foreach (var comp in selected.Components) {
                    if (comp is ICopyableComp cop && cop.IsCopyable) {
                        A.S.Query.DeselectAny.Ask();
                        SwitchToCopy(cop);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
