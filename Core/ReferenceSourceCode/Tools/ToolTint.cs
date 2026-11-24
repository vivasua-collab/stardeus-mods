using Game.Constants;
using Game.Data;
using KL.Grid;
using UnityEngine;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using Game.Input;
using KL.Text;
using Game.UI;
using Game.Systems.Hints;
using Game.Systems;
using Game.Visuals;
using KL.Utils;
using KL.Randomness;

namespace Game.Tools {
    public sealed class ToolTint : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolTint());
        }
        public override bool SkipMenu => true;
        protected override int lineModeCursor => CursorTypeH.CursorEyeDropper;
        // Line mode is color picker
        private bool isInColorPickerMode => isInLineMode;
        public override int Cursor {
            get {
                switch (mode) {
                    case "erase": return CursorTypeH.CursorEraser;
                    case "copy": return lineModeCursor;
                    default: return CursorTypeH.CursorPaint;
                }
            }
        }
        private readonly List<string> availableParams = new() {
            "draw",
            "erase",
            "copy"
        };
        public const string ToolId = "tint";

        public override string Id => ToolId;

        public override bool IsConfigurable => true;

        private string mode = "draw";
        protected override bool WallOrFloorCheck(int idx) {
            if (idx < Consts.MapMargin || idx >= S.GridSize - Consts.MapMargin) {
                return false;
            }
            var areaAt = S.Sys.Areas.At(lastHoverPos);
            var areaAt2 = S.Sys.Areas.At(idx);
            D.Warn("Areas FF: {0}->{1}", areaAt, areaAt2);
            if (areaAt == 0) { return false; }
            if (areaAt2 == 0) { return false; }
            return areaAt == areaAt2;
        }
        public override List<string> AvailableParams => availableParams;

        public override string MenuIcon => null;

        public Color ToolColor;
        private Color toolPreviewColor {
            get {
                var c = ToolColor;
                if (mode == "erase" || isInDeleteMode) {
                    c = Consts.ColorWhite;
                } else if (mode == "copy" || isInColorPickerMode) {
                    if (TryGetTopmostColor(lastHoverPos, out var tint)) {
                        return tint;
                    }
                    return Consts.ColorTransparent;
                }
                c.a *= 0.5f;
                return c;
            }
        }
        public int ToolLayer;
        public void Configure(int layer, Color color, string mode) {
            ToolLayer = layer;
            ToolColor = color;
            The.Toolbox.SetActive(this, mode);
        }

        public override bool Configure(string param) {
            lastHoverPos = -1;
            mode = param;
            return true;
        }

        public override void Activate() {
            base.Activate();
            S.Sig.ShowOverlay.Send(OverlayId.Tint);
        }
        private XParticles mouseRectParticles;

        public override void AddParticles(Dictionary<int, XParticles> x) {
            if (isHoverHidden) { return; }
            if (mouseRectParticles == null) {
                x.TryGetValue(ParticlesIdH.MouseRect, out mouseRectParticles);
            }
            if (mouseRectParticles == null) { return; }
            var ps = mouseRectParticles;
            var psc = AddModeParticles(ps.Particles, 0);

            if (drawRect) {
                var dpc = dragPositions.Count;
                posWantToTint.Clear();
                int maxTiles = int.MaxValue;
                if (mode == "draw") {
                    maxTiles = S.Sys.Tint.EstimateRemainingTiles(ToolColor, true);
                }

                if (dpc < 400) {
                    for (int i = 0; i < dpc; i++) {
                        int pos = dragPositions[i];
                        if (isInDeleteMode) {
                            posWantToTint.Add(pos);
                        } else {
                            if (IsAbleToPlace(pos)) {
                                posWantToTint.Add(pos);
                            }
                        }
                    }

                    if (ps.Particles.Length <= psc + posWantToTint.Count) {
                        // We can't show more particles
                        psc += dpc;
                    } else {
                        Color color;
                        if (posWantToTint.Count > maxTiles) {
                            color = Consts.RedRectColor;
                        } else {
                            color = toolPreviewColor;
                        }
                        foreach (var p in posWantToTint) {
                            var position = Pos.ToVector(p);
                            ps.Particles[psc++] = ParticlesSys.ParticleAt(
                                position, color, 1, 0.1f);
                        }
                    }
                } else {
                    Color color;
                    if (dpc > maxTiles) {
                        color = Consts.RedRectColor;
                    } else {
                        color = toolPreviewColor;
                    }
                    if (ps.Particles.Length > psc) {
                        ps.Particles[psc++] = ParticlesSys.ParticleAt(lastRect.Center(),
                            color, lastRect.Extents, 0.1f);
                    } else {
                        psc++;
                    }
                }
            }

            ps.ShowParticles(psc, false);
        }
        private int AddModeParticles(ParticleSystem.Particle[] p, int offset) {
            offset = DrawGuidelines(p, offset, dragEnd);

            if (isInBucketMode && bucketFill.Count > 0) {
                posWantToTint.Clear();
                DrawBucketFill((pos) => {
                    posWantToTint.Add(pos);
                });
                Color color = toolPreviewColor;
                if (mode == "draw") {
                    if (posWantToTint.Count > S.Sys.Tint.EstimateRemainingTiles(ToolColor, true)) {
                        color = Consts.RedRectColor;
                    }
                }
                foreach (var pos in posWantToTint) {
                    if (p.Length > offset) {
                        p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos),
                            color, RenderUtils.TilePulse, 0.1f);
                    } else {
                        offset++;
                    }
                }
            } else if (isDragging) {
                offset = DrawGuidelines(p, offset, dragStart);
                if (isInCircleMode) {
                    posWantToTint.Clear();
                    DrawCircle((pos) => {
                        if (IsAbleToPlace(pos)) {
                            posWantToTint.Add(pos);
                        }
                    });
                    Color color = toolPreviewColor;
                    if (mode == "draw") {
                        if (posWantToTint.Count > S.Sys.Tint.EstimateRemainingTiles(ToolColor, true)) {
                            color = Consts.RedRectColor;
                        }
                    }
                    foreach (var pos in posWantToTint) {
                        offset = DrawCellChecked(p, offset, pos, color);
                    }
                }
            } else {
                if (EntityUtils.IsWithinBounds(lastHoverPos)) {
                    Color color = toolPreviewColor;
                    if (mode == "draw") {
                        if (S.Sys.Tint.EstimateRemainingTiles(ToolColor, true) < 1) {
                            color = Consts.RedRectColor;
                        }
                    }
                    p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(lastHoverPos),
                        color, RenderUtils.TilePulse, 0.1f);
                }
            }

            return offset;
        }
        private void CheckNonDragModesForTint(int pos) {
            if (CheckBucketMode() || CheckCircleMode() || CheckLineMode() || CheckPencilMode() || CheckDeleteMode()) {
                if (isInBucketMode && (needsBucketFF || pos != lastHoverPos)) {
                    needsBucketFF = false;
                    bucketFill.Clear();
                    var area = S.Sys.Areas.At(pos);
                    HashSet<int> areaTiles = null;
                    if (area > 0) {
                        areaTiles = S.Sys.Areas.Areas[area];
                    } else if (area < -1) {
                        areaTiles = S.Sys.Areas.OuterAreas[area];
                    }
                    if (!Arrays.IsEmpty(areaTiles)) {
                        var expanded = PosRect.Expand(areaTiles);
                        if (ToolLayer == WorldLayer.Floor) {
                            foreach (var p in expanded) {
                                if (EntityUtils.IsFloorAt(p)) {
                                    bucketFill.Add(p);
                                }
                            }
                        } else if (ToolLayer == WorldLayer.Walls) {
                            foreach (var p in expanded) {
                                if (EntityUtils.IsWallAt(p)) {
                                    bucketFill.Add(p);
                                }
                            }
                            Caches.IntSet.Clear();
                        }
                    }
                }
            }
        }
        private bool IsAbleToPlace(int pos) {
            if (ToolLayer == WorldLayer.Floor) {
                if (mode == "draw") {
                    return EntityUtils.IsConstructedFloorAt(pos);
                } else {
                    return S.Sys.Tint.TryGetFloorTint(pos, out _);
                }
            } else if (ToolLayer == WorldLayer.Walls) {
                if (mode == "draw") {
                    return EntityUtils.IsConstructedWallAt(pos);
                } else {
                    return S.Sys.Tint.TryGetWallTint(pos, out _);
                }
            }
            D.Err("Unexpected tool layer: {0}", ToolLayer);
            return false;
        }

        private int DrawCellChecked(ParticleSystem.Particle[] p, int offset, int pos, Color color) {
            if (p.Length > offset) {
                p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos), color,
                    RenderUtils.TilePulse, 0.1f);
            } else {
                offset++;
            }
            return offset;
        }

        public override void OnHover(Vector2 coords) {
            if (!EntityUtils.IsWithinBounds(coords)) { return; }
            isHoverHidden = false;
            OnHoverCommon(coords, true);
            var pos = EntityUtils.ToPosSafe(coords);
            if (isDragging && !isInColorPickerMode) {
                CheckDragModes();
                OnHoverDrag();
                return;
            } else {
                dragEnd = pos;
                CheckNonDragModesForTint(pos);
            }
            /*
            if (pos != lastHoverPos) {
                Color color = toolPreviewColor;
                if (mode == "draw") {
                    if (S.Sys.Tint.EstimateRemainingTiles(ToolColor, true) < 1) {
                        color = Consts.RedRectColor;
                    }
                }
                ShowColorTile(pos, color);
            }
            */
            lastHoverPos = pos;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            if (!wasClickedDown) { return; }
            OnDragCommon(start, end, true);
            if (isInCircleMode) {
                return;
            } else if (isInColorPickerMode) {
                drawRect = false;
                return;
            }
            if (isInPencilMode) {
                if (lastPencilTile == Pos.Invalid) {
                    DrawAtWithEffect(dragEnd);
                } else if (lastPencilTile != dragEnd) {
                    DrawLineCustom(EntityUtils.CenterOf(lastPencilTile), end,
                        DrawAtWithEffect);
                }
                lastPencilTile = dragEnd;
                return;
            }
            PosRect rect = DragRect(start, end);

            HandleDragRect(rect, toolPreviewColor);
        }

        public override void Deactivate() {
            S.Sys.Tint.OnToolClose();
            base.Deactivate();
        }

        public override void OnClickDown(Vector2 pos) {
            var isCopy = mode == "copy" || isInColorPickerMode;
            if (!isCopy) {
                wasClickedDown = true;
                return;
            }
            if (TryGetTopmostColor(lastHoverPos, out var tint)) {
                S.Sys.Tint.PickToolColor(tint);
                UISounds.PlayDigitalConfirm();
            } else {
                UISounds.PlayActionDenied();
            }
        }

        private bool TryGetTopmostColor(int pos, out Color color) {
            if (S.Sys.Tint.TryGetWallTint(lastHoverPos, out color)) {
                return true;
            } else if (S.Sys.Tint.TryGetFloorTint(lastHoverPos, out color)) {
                return true;
            }
            return false;
        }

        private readonly HashSet<int> posNeedingVFX = new();
        private readonly HashSet<int> posWantToTint = new();

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (!wasClickedDown) { return; }
            posNeedingVFX.Clear();
            posWantToTint.Clear();
            wasClickedDown = false;
            if (isInPencilMode) {
                lastPencilTile = Pos.Invalid;
            }
            if (isInBucketMode) {
                for (int i = bucketFill.Count - 1; i >= 0; i--) {
                    int p = bucketFill[i];
                    ActivateToolAt(p);
                }
                bucketFill.Clear();
            } else if (isDragging && isInCircleMode) {
                dragInfoText?.SetText(null);
                DrawCircle(ActivateToolAt);
                var r = Mathf.CeilToInt(radius);
                var rekt = new PosRect(Pos.NeighborAt(dragStart, -r, -r),
                    Pos.NeighborAt(dragStart, r, r));
            } else {
                if (!isInPencilMode) {
                    if (isDragging) {
                        foreach (var pos in dragPositions) {
                            ActivateToolAt(pos);
                        }
                    } else {
                        var pos = EntityUtils.ToPos(position);
                        // Single spot
                        DrawAtWithEffect(pos);
                        lastHoverPos = Pos.Invalid;
                    }
                }
            }

            if (posWantToTint.Count > 0) {
                if (S.Sys.Tint.EstimateRemainingTiles(ToolColor, true)
                        < posWantToTint.Count) {
                    UISounds.PlayActionDenied();
                } else {
                    foreach (var pos in posWantToTint) {
                        if (S.Sys.Tint.TryTint(ToolLayer, pos, ToolColor, false)) {
                            posNeedingVFX.Add(pos);
                        }
                    }
                }
            }
            if (posNeedingVFX.Count > 0) {
                Color fxColor = mode == "draw" ? ToolColor : Consts.ColorWhite;
                S.Sys.Tint.ShowParticlesAt(Pos.Encapsulate(posNeedingVFX), fxColor);
            }

            posWantToTint.Clear();
            ClearTexts();
            ClearDragPositions();
            isDragging = false;
        }

        private void ClearTexts() {
            if (posTextDragStart != null) {
                posTextDragStart.SetText(null);
            }
            if (posText != null) {
                posText.SetText(null);
            }
        }

        private void DrawAtWithEffect(int pos) {
            if (!EntityUtils.IsWithinMargin(pos, 1, 1)) { return; }
            if (S.Sys.Tint.IsTintHidden) { return; }
            if (mode == "erase" || isInDeleteMode) {
                S.Sys.Tint.RemoveTint(ToolLayer, pos, true);
            } else {
                if (IsAbleToPlace(pos)) {
                    if (!S.Sys.Tint.TryTint(ToolLayer, pos, ToolColor, true)) {
                        UISounds.PlayActionDenied();
                    }
                }
            }
        }

        private void ActivateToolAt(int pos) {
            if (!EntityUtils.IsWithinMargin(pos, 1, 1)) { return; }
            if (S.Sys.Tint.IsTintHidden) { return; }
            if (mode == "erase" || isInDeleteMode) {
                if (S.Sys.Tint.RemoveTint(ToolLayer, pos, false)) {
                    posNeedingVFX.Add(pos);
                }
            } else {
                if (IsAbleToPlace(pos)) {
                    posWantToTint.Add(pos);
                }
            }
        }

        public override string ToString() {
            return Id;
        }
    }
}
