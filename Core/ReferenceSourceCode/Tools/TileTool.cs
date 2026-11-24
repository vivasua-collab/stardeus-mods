using Game.Constants;
using System.Collections.Generic;
using Game.Data;
using KL.Grid;
using KL.Utils;
using Game.Rendering;
using UnityEngine;
using KL.Text;
using System;
using System.Threading;
using System.Collections;
using KL.Algos;
using KL.Collections;
using Game.Input;
using Game.Systems;
using Unity.Mathematics;
using Game.Visuals;
using Game.CodeGen;

namespace Game.Tools {
    public abstract class TileTool : Tool {
        protected enum ToolMode {
            None,
            Circle,
            Bucket,
            Line,
            Pencil,
            Delete,
            Special,
        }
        private ToolMode wantedMode = ToolMode.None;
        public override void CycleMode() {
            wantedMode = (ToolMode) (((int) wantedMode + 1) % 6);
        }
        protected virtual int lineModeCursor => CursorTypeH.CursorLine;
        protected virtual int specialModeCursor => 0;
        protected PosRect lastRect;
        protected GameTextTMP dragInfoText;
        protected int lastHoverPos;
        protected readonly List<int> dragPositions = new();
        protected bool drawRect;
        protected Color rectColor;
        protected virtual Color markedColor => Consts.RedMarkedTileColor;
        protected bool showDragRectSize = true;
        protected bool wasClickedDown;
        protected int dragStart;
        protected int dragEnd;
        protected Vector2 rawDragStart;
        protected Vector2 dragStartV;
        protected Vector2 dragEndV;
        protected Vector2 dragStartBL;
        protected Vector2 dragEndBL;
        protected float radius;
        protected bool isCtrlPressed;
        protected bool isShiftPressed;
        protected bool isAltPressed;
        protected bool wantsLineMode => wantedMode == ToolMode.Line
            || (isCtrlPressed && !isShiftPressed && !isAltPressed);
        protected bool wantsCircleMode => wantedMode == ToolMode.Circle
            || (!isCtrlPressed && isShiftPressed && !isAltPressed);
        protected bool wantsBucketMode => wantedMode == ToolMode.Bucket
            || (isCtrlPressed && isShiftPressed && !isAltPressed);
        protected bool wantsPencilMode => wantedMode == ToolMode.Pencil
            || (!isCtrlPressed && !isShiftPressed && isAltPressed);
        protected bool wantsDeleteMode => wantedMode == ToolMode.Delete
            || (isCtrlPressed && !isShiftPressed && isAltPressed);
        protected bool wantsSpecialMode => wantedMode == ToolMode.Special
            || (isCtrlPressed && isShiftPressed && isAltPressed);
        protected bool needsBucketFF;
        protected bool isInBucketMode;
        public bool IsInBucketMode => isInBucketMode;
        protected bool isInPencilMode;
        public bool IsInPencilMode => isInPencilMode;
        protected bool isInSpecialMode;
        protected bool isInLineMode;
        public override int HoverPos => IsDragging ? dragEnd : lastHoverPos;
        public override bool IsClickedDown => wasClickedDown;
        public bool IsInLineMode => isInLineMode;
        protected bool isInCircleMode;
        public bool IsInCircleMode => isInCircleMode;
        protected bool isInDeleteMode;
        public bool IsInDeleteMode => isInDeleteMode;
        protected readonly List<int> bucketFill = new();
        protected CancellationTokenSource ffCancel;
        protected readonly List<int> markedTiles = new();
        protected GameTextTMP posText;
        protected GameTextTMP posTextDragStart;
        protected int lastPencilTile = Pos.Invalid;
        protected bool isDrawBuildGuides;
        protected bool isShowingMarkedTiles;
        // Set while the cursor is over UI. Hover feedback left where the cursor left
        // the map reads as if a click would still land there. A subclass clears it
        // first thing in OnHover, and skips hover feedback it draws per frame while set
        protected bool isHoverHidden;

        public override void OnHoverUI() {
            if (isHoverHidden) { return; }
            isHoverHidden = true;
            HideHover();
        }

        // Takes down what the tool shows for the hovered tile. The next OnHover
        // brings it back, also on the same tile
        protected virtual void HideHover() {
            ClearPreview();
            lastHoverPos = Pos.Invalid;
        }

        protected void AddMarkedTile(int pos) {
            markedTiles.Add(pos);
            isShowingMarkedTiles = true;
        }
        protected bool RemoveMarkedTile(int posIdx) {
            return markedTiles.Remove(posIdx);
        }

        public override void Activate() {
            wantedMode = ToolMode.None;
            base.Activate();
            isDrawBuildGuides = The.Prefs.GetBool(Pref.GShowBuildGuidelines, PrefH.GShowBuildGuidelines, true);
        }

        // WARNING: must call base.Activate if overriding Activate in subtool
        // for this to work!
        protected int DrawGuidelines(ParticleSystem.Particle[] p, int offset,
                int rootPos, bool skipDrawCheck = false) {
            if (!isDrawBuildGuides && !skipDrawCheck) {
                //D.Warn("Skipping guidelines: {0} / {1}", isDrawBuildGuides, skipDrawCheck);
                return offset;
            }
            // Draw vertical and horizontal line from the cursor
            Pos.ToXY(rootPos, out var rx, out var ry);
            for (int x = 0; x < S.GridWidth; x++) {
                var pos = Pos.FromXY(x, ry);
                var color = x % 10 == 0 ?
                    Consts.PlanGuidelinesAccentColor : Consts.PlanGuidelinesColor;
                if (pos != rootPos) {
                    offset = DrawCellPulse(p, offset, pos, color);
                }
            }
            for (int y = 0; y < S.GridHeight; y++) {
                if (y == ry) { continue; }
                var pos = Pos.FromXY(rx, y);
                var color = y % 10 == 0 ?
                    Consts.PlanGuidelinesAccentColor : Consts.PlanGuidelinesColor;
                if (pos != rootPos) {
                    offset = DrawCellPulse(p, offset, pos, color);
                }
            }
            return offset;
        }

        protected int DrawCellPulse(ParticleSystem.Particle[] p, int offset, int pos, Color color) {
            if (Pos.IsOOB(pos)) { return offset; }
            if (p.Length > offset) {
                var position = Pos.ToVector(pos);
                p[offset++] = ParticlesSys.ParticleAt(position, color,
                    RenderUtils.TilePulse, 0.1f);
            } else {
                offset++;
            }
            return offset;
        }

        protected void ShowPosText(Vector2 c, Vector2 offset, ref GameTextTMP posText) {
            var textPos = c + offset * RenderUtils.CameraOrtho;
            if (posText == null) {
                posText = GameTextTMP.Create("ToolRectSizeText", "RectSize", textPos);
            } else {
                posText.MoveTo(textPos);
            }
            posText.SetText(Pos.String(EntityUtils.ToPosSafe(c)));
            posText.SetFontSize(RenderUtils.CameraOrtho * Consts.ToolRectFontSize * 0.1f);
        }

        protected bool CheckDragModes() {
            return CheckCircleMode() || CheckLineMode() || CheckSpecialMode();
        }

        protected void CheckNonDragModes(int pos) {
            if (CheckBucketMode() || CheckCircleMode() || CheckLineMode() || CheckPencilMode() || CheckDeleteMode() || CheckSpecialMode()) {
                // Well. Switched into one of those modes, that's it
                if (isInBucketMode && (needsBucketFF || pos != lastHoverPos)) {
                    // Don't redo the check if we change the hover position
                    // but remain in the fill zone
                    if (needsBucketFF || !bucketFill.Contains(pos)) {
                        needsBucketFF = false;
                        bool trySameFrame = false;
                        #if !PRODUCTION_BUILD
                        if (The.TestsAreRunning) {
                            trySameFrame = true;
                        }
                        #endif
                        Coroutines.Start(RunBucketFFPreview(pos, trySameFrame),
                            this, "BucketFF", trySameFrame: trySameFrame, false);
                    }
                }
            }
        }
        protected virtual bool WallOrFloorCheck(int idx) {
            if (idx < Consts.MapMargin || idx >= S.GridSize - Consts.MapMargin) {
                return false;
            }
            return S.Map.Grids[WorldLayer.Floor].Data[idx] != null ||
                S.Map.Grids[WorldLayer.Walls].Data[idx] != null ||
                S.Map.Grids[WorldLayer.Objects].Data[idx] != null;
        }
        protected virtual bool BoundaryCheck(int idx) {
            if (idx < Consts.MapMargin || idx >= S.GridSize - Consts.MapMargin) {
                return true;
            }
            return false;
        }
        protected IEnumerator RunBucketFFPreview(int pos, bool sync) {
            bucketFill.Clear();
            if (ffCancel != null) {
                D.Warn("Already running FF, cancelling! Will run at pos: {0}", Pos.String(pos));
                ffCancel.Cancel();
                ffCancel = null;
            }
            var ff = new FloodFillParallel<int>(pos, WallOrFloorCheck, BoundaryCheck,
                Pos.NeighborAt, true, includeDiagonal: false);
            var task = ff.ProcessAsync(out ffCancel);
            if (sync) {
                task.Wait();
            } else {
                while (!task.IsCompleted) {
                    yield return null;
                    if (!GameState.IsCurrent(S)) { yield break; }
                }
            }
            if (!task.IsCanceled) {
                // D.Warn("Done Bucket FF. Found {0} points. Edge? {1}",
                //    ff.Result.Count, ff.ReachedTheEdge);
                // TODO If we comment the ff.ReachedTheEdge, vine patterns will appear
                if (!ff.ReachedTheEdge) {
                    bucketFill.AddRangeUnique(ff.Result);
                }
                ffCancel = null;
            }
        }
        protected bool CheckDeleteMode() {
            if (wantsDeleteMode) {
                if (!isInDeleteMode) {
                    isInDeleteMode = true;
                    The.SysSig.SetCursor.Send(CursorTypeH.CursorEraser);
                    drawRect = false;
                    dragPositions.Clear();
                }
                return true;
            } else {
                if (isInDeleteMode) {
                    The.SysSig.SetCursor.Send(Cursor);
                    isInDeleteMode = false;
                }
            }
            return false;
        }
        protected bool CheckSpecialMode() {
            if (specialModeCursor == 0) { return false; }
            if (wantsSpecialMode) {
                if (!isInSpecialMode) {
                    isInSpecialMode = true;
                    The.SysSig.SetCursor.Send(specialModeCursor);
                    drawRect = false;
                    dragPositions.Clear();
                }
                return true;
            } else {
                if (isInSpecialMode) {
                    The.SysSig.SetCursor.Send(Cursor);
                    isInSpecialMode = false;
                }
            }
            return false;
        }
        protected bool CheckPencilMode() {
            if (wantsPencilMode) {
                if (!isInPencilMode) {
                    lastPencilTile = Pos.Invalid;
                    isInPencilMode = true;
                    The.SysSig.SetCursor.Send(CursorTypeH.CursorPencil);
                    drawRect = false;
                    dragPositions.Clear();
                }
                return true;
            } else {
                if (isInPencilMode) {
                    The.SysSig.SetCursor.Send(Cursor);
                    isInPencilMode = false;
                }
            }
            return false;
        }
        protected bool CheckBucketMode() {
            if (wantsBucketMode) {
                if (!isInBucketMode) {
                    needsBucketFF = true;
                    isInBucketMode = true;
                    The.SysSig.SetCursor.Send(CursorTypeH.CursorBucket);
                    drawRect = false;
                    dragPositions.Clear();
                }
                return true;
            } else {
                if (isInBucketMode) {
                    The.SysSig.SetCursor.Send(Cursor);
                    isInBucketMode = false;
                }
            }
            return false;
        }
        protected bool CheckCircleMode() {
            if (wantsCircleMode) {
                if (!isInCircleMode) {
                    isInCircleMode = true;
                    The.SysSig.SetCursor.Send(CursorTypeH.CursorCircle);
                    drawRect = false;
                    dragPositions.Clear();
                }
                return true;
            } else {
                if (isInCircleMode) {
                    The.SysSig.SetCursor.Send(Cursor);
                    isInCircleMode = false;
                }
            }
            return false;
        }
        protected bool CheckLineMode() {
            if (wantsLineMode) {
                if (!isInLineMode) {
                    isInLineMode = true;
                    The.SysSig.SetCursor.Send(lineModeCursor);
                    drawRect = false;
                    dragPositions.Clear();
                }
                return true;
            } else {
                if (isInLineMode) {
                    The.SysSig.SetCursor.Send(Cursor);
                    isInLineMode = false;
                }
            }
            return false;
        }
        protected void ShowCircleSizeText() {
            if (radius < 2) {
                if (dragInfoText != null) {
                    dragInfoText.SetText(null);
                }
            }
            else {
                var c = (dragStartV + dragEndV) / 2f;
                if (dragInfoText == null) {
                    dragInfoText = GameTextTMP.Create("ToolRectSizeText", "RectSize", c);
                } else {
                    dragInfoText.MoveTo(c);
                }
                dragInfoText.SetText("r={0:0}", radius);
                dragInfoText.SetFontSize(RenderUtils.DistanceToCam * 0.3f *
                    Consts.ToolRectFontSize);
            }
        }
        private void ShowLineLengthText() {
            if (radius < 2) {
                if (dragInfoText != null) {
                    dragInfoText.SetText(null);
                }
            }
            else {
                var c = (dragStartV + dragEndV) / 2f;
                if (dragInfoText == null) {
                    dragInfoText = GameTextTMP.Create("ToolRectSizeText", "RectSize", c);
                } else {
                    dragInfoText.MoveTo(c);
                }
                dragInfoText.SetText("{0:0.#}", radius);
                dragInfoText.SetFontSize(RenderUtils.DistanceToCam * 0.3f * Consts.ToolRectFontSize);
            }
        }


        protected void DrawBucketFill(Action<int> posAction) {
            for (int i = bucketFill.Count - 1; i >= 0; i--) {
                int p = bucketFill[i];
                posAction(p);
            }
        }

        protected void DrawCircle(Action<int> posAction) {
            Pos.ForEachInCircle(dragStart, radius, true, posAction);
            ShowCircleSizeText();
        }

        protected void DrawLineCustom(Vector2 dragStartV, Vector2 dragEndV,
                Action<int> posAction) {
            ForEachLineTile(EntityUtils.ToPos(dragStartV),
                EntityUtils.ToPos(dragEndV), posAction);
        }

        protected void DrawLine(Action<int> posAction) {
            ShowLineLengthText();
            ForEachLineTile(dragStart, dragEnd, posAction);
        }

        private static void ForEachLineTile(int startPos, int endPos,
                Action<int> posAction) {
            Pos.ToXY(startPos, out var x0, out var y0);
            Pos.ToXY(endPos, out var x1, out var y1);
            var dx = Math.Abs(x1 - x0);
            var dy = Math.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx - dy;
            var x = x0;
            var y = y0;
            while (true) {
                posAction(Pos.FromXY(x, y));
                if (x == x1 && y == y1) { break; }
                var e2 = 2 * err;
                var oldX = x;
                if (e2 > -dy) {
                    err -= dy;
                    x += sx;
                }
                if (e2 < dx) {
                    if (x != oldX) {
                        // Diagonal step: insert the corner tile so that
                        // consecutive tiles stay orthogonally connected
                        posAction(Pos.FromXY(x, y));
                    }
                    err += dx;
                    y += sy;
                }
            }
        }

        public override void UpdateVFX(ParticlesSys ps) {
            if (markedTiles.Count > 0) {
                RenderMarkedTiles(ps.SelectionVFX.MarkedTiles);
            } else if (isShowingMarkedTiles) {
                ps.SelectionVFX.MarkedTiles.Stop();
                isShowingMarkedTiles = false;
            }
        }

        private XParticles mouseRectParticles;
        protected XParticles GetMouseRectParticles(Dictionary<int, XParticles> xParticles) {
            if (mouseRectParticles == null) {
                xParticles.TryGetValue(ParticlesIdH.MouseRect, out mouseRectParticles);
            }
            return mouseRectParticles;
        }

        public override void AddParticles(Dictionary<int, XParticles> x) {
            var rectP = GetMouseRectParticles(x);
            int offset = AddRectParticles(0, rectP);
            rectP.ShowParticles(offset, false);
        }

        protected virtual int AddRectParticles(int offset, XParticles rectP) {
            if (!drawRect) { return offset; }
            ParticleSystem.Particle particle;
            if (isInCircleMode) {
                particle = ParticlesSys.ParticleAt(EntityUtils.ToVector2(lastHoverPos),
                    rectColor, 0.5f, 0.1f);
            } else {
                particle = ParticlesSys.ParticleAt(lastRect.Center(),
                    rectColor, lastRect.Extents, 0.1f);
            }
            rectP.Particles[offset++] = particle;
            return offset;
        }

        private void RenderMarkedTiles(VFXTiles fx) {
            isShowingMarkedTiles = true;
            var count = 0;
            var color = new float4(markedColor.r, markedColor.g, markedColor.b, markedColor.a);
            for (int i = 0; i < markedTiles.Count; i++) {
                int tile = markedTiles[i];
                count = fx.AddTile(tile, color, 1f, count);
            }
            fx.Show(count);
        }

        protected void ShowRedTile(int pos) {
            lastRect = new(pos, pos);
            drawRect = true;
            rectColor = Consts.RedRectColor;
        }
        protected void ShowGreenTile(int pos) {
            lastRect = new(pos, pos);
            drawRect = true;
            rectColor = Consts.GreenRectColor;
        }
        protected void ShowColorTile(int pos, Color color) {
            lastRect = new PosRect(pos, pos);
            drawRect = true;
            rectColor = color;
        }

        public override void OnClickDown(Vector2 pos) {
            wasClickedDown = true;
        }

        protected void ClearPreview() {
            drawRect = false;
            if (dragInfoText != null) {
                dragInfoText.Destroy();
                dragInfoText = null;
            }
            if (posText != null) {
                posText.Destroy();
                posText = null;
            }
            if (posTextDragStart != null) {
                posTextDragStart.Destroy();
                posTextDragStart = null;
            }
        }

        public override void Deactivate() {
            wasClickedDown = false;
            isHoverHidden = false;
            wantedMode = ToolMode.None;
            isInBucketMode = false;
            isInPencilMode = false;
            isInLineMode = false;
            isInCircleMode = false;
            isInDeleteMode = false;
            isInSpecialMode = false;
            ClearPreview();
            if (isDragging) {
                isDragging = false;
                ClearDragPositions();
            }

            markedTiles.Clear();
            if (isShowingMarkedTiles) {
                isShowingMarkedTiles = false;
                S.Sys.Particles.SelectionVFX.MarkedTiles.Stop();
            }
        }
        public override bool CancelDrag() {
            if (!isDragging) { return false; }
            wasClickedDown = false;
            ClearDragPositions();
            isDragging = false;
            return true;
        }

        protected int AddDragRectParticles(ParticleSystem.Particle[] particles,
                Color color, Vector2 dragStart, Vector2 dragEnd) {
            if (!isDragging) { return 0; }
            var mid = (dragStart + dragEnd) / 2f;
            particles[0] = ParticlesSys.ParticleAt(mid, color,
                Vectors.Abs(dragEnd - dragStart), 0.1f);
            return 1;
        }

        protected void UpdateMarkedTiles(int pos, int radius = 10) {
            markedTiles.Clear();
            Pos.ForEachInRadius(pos, radius, true, (p) => {
                if (IsTileMarked(p)) {
                    markedTiles.Add(p);
                }
                return true;
            });
        }

        protected virtual bool IsTileMarked(int pos) {
            return false;
        }
        protected void OnHoverCommon(Vector2 coords, bool skipDrawCheck = false) {
            if (isDrawBuildGuides || skipDrawCheck) {
                ShowPosText(coords, new Vector2(0.1f, 0f), ref posText);
            }
            isCtrlPressed = The.Bindings.IsPressed(
                ActionType.CtrlModifier);
            isShiftPressed = The.Bindings.IsPressed(
                ActionType.ShiftModifier);
            isAltPressed = The.Bindings.IsPressed(
                ActionType.AltModifier);
        }

        protected void OnDragCommon(Vector2 start, Vector2 end, bool skipDrawCheck = false) {
            if (isDrawBuildGuides || skipDrawCheck) {
                ShowPosText(start, new Vector2(-0.1f, 0.1f), ref posTextDragStart);
            }
            rawDragStart = start;
            dragStart = EntityUtils.ToPosSafe(start);
            dragEnd = EntityUtils.ToPosSafe(end);
            dragStartV = EntityUtils.CenterOf(dragStart);
            dragStartBL = EntityUtils.ToVector2(dragStart);
            dragEndV = EntityUtils.CenterOf(dragEnd);
            dragEndBL = EntityUtils.ToVector2(dragEnd);
            radius = Vector2.Distance(end, start);
            isDragging = true;
        }

        protected void OnHoverDrag() {
            if (ToolGrid.IsWithinBounds(lastHoverPos)) {
                ToolGrid.Set(lastHoverPos, null);
                lastHoverPos = Pos.Invalid;
            }
        }

        protected PosRect DragRow(Vector2 start, Vector2 end) {
            var xDiff = System.Math.Abs(end.x - start.x);
            var yDiff = System.Math.Abs(end.y - start.y);
            var heading = end - start;
            Vector2 projectedEnd;
            if (xDiff > yDiff) {
                projectedEnd = Vector3.Project(heading, Vector2.right);
            } else {
                projectedEnd = Vector3.Project(heading, Vector2.up);
            }
            projectedEnd = start + projectedEnd;
            var startPos = EntityUtils.ToPosSafe(start);
            var endPos = EntityUtils.ToPosSafe(projectedEnd);
            return ClampRect(startPos, endPos);
        }

        protected PosRect DragRect(Vector2 start, Vector2 end) {
            return ClampRect(
                EntityUtils.ToPosSafe(start),
                EntityUtils.ToPosSafe(end));
        }

        protected int ClampPos(int pos, int rw, int rh, int mapMargin) {
            var m = mapMargin;
            var maxX = S.GridWidth - rw - m;
            var maxY = S.GridHeight - rh - m;
            Pos.ToXY(pos, out var x, out var y);
            if (x >= m && x <= maxX && y >= m && y <= maxY) {
                return pos;
            }
            x = Mathf.Clamp(x, m, maxX);
            y = Mathf.Clamp(y, m, maxY);
            return Pos.FromXY(x, y);
        }

        private PosRect ClampRect(int start, int end) {
            // It's not good, because we have derelict zones we may want to repair or fix
            var m = 0; //Consts.MapMargin;
            var maxX = S.GridWidth - 1 - m;
            var maxY = S.GridHeight - 1 - m;
            return PosRect.Clamped(start, end, m, maxX, m, maxY);
        }

        protected void HandleDragRect(PosRect rect, Color color) {
            if (lastRect.Equals(rect)) { return; }
            var clampedRect = ClampRect(rect.Min, rect.Max);
            ShowRectSizeText(clampedRect);

            lastRect = clampedRect;
            dragPositions.Clear();
            drawRect = true;
            rectColor = color;

            lastRect.ForEach((p) => { dragPositions.Add(p); });
        }

        private void ShowRectSizeText(PosRect rect) {
            if (!showDragRectSize) { return; }
            var rectCenter = rect.Center(Consts.GridTileSize);
            if (rect.Size < 2) {
                if (dragInfoText != null) {
                    dragInfoText.SetText(null);
                }
            }
            else {
                if (dragInfoText == null) {
                    dragInfoText = GameTextTMP.Create("ToolRectSizeText", "RectSize",
                        rectCenter);
                } else {
                    dragInfoText.MoveTo(rectCenter);
                }
                dragInfoText.SetText("{0}x{1}", rect.Width, rect.Height);
                dragInfoText.SetFontSize(RenderUtils.DistanceToCam * 0.3f * Consts.ToolRectFontSize);
            }
        }

        protected void ClearDragPositions() {
            dragPositions.Clear();
            ClearPreview();
        }

        public override string ToString() {
            return Id;
        }
    }
}
