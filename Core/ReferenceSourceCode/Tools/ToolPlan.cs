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
using Game.Systems.UIUX;
using Game.UI;
using Game.Systems.Hints;
using Game.Systems;
using Game.Visuals;
using KL.Utils;

namespace Game.Tools {
    public sealed class ToolPlan : TileTool {
        [SelfInit]
        private static void Register() {
            Toolbox.Add(ToolId, () => new ToolPlan());
        }
        public override bool SkipMenu => true;
        private UIGuidelines guideX;
        private UIGuidelines guideY;
        private int mirrorPointX;
        private int mirrorPointY;
        protected override int specialModeCursor => CursorTypeH.CursorMove;
        public bool MirrorEven;
        public void SetMirrorPoint(int x, int y) {
            MirrorPoint = Pos.FromXY(x, y);
            mirrorPointX = x;
            mirrorPointY = y;
        }
        public void SetMirrorPoint(int pos) {
            MirrorPoint = pos;
            if (pos > 0 && pos < Pos.GridSize) {
                Pos.ToXY(pos, out mirrorPointX, out mirrorPointY);
            } else {
                SetMirrorPoint(Pos.GridW / 2, Pos.GridH / 2);
            }
        }
        public int MirrorPoint;
        private bool mirrorX;
        private bool mirrorY;
        public BBool MirrorXB = new();
        public BBool MirrorYB = new();
        public override int Cursor {
            get {
                switch (mode) {
                    case "clear": return CursorTypeH.CursorEraser;
                    case "ruler": return CursorTypeH.CursorRuler;
                    case "line": return CursorTypeH.CursorLine;
                    case "pencil": return CursorTypeH.CursorPencil;
                    case "rect": return CursorTypeH.CursorPaint;
                    case "circle": return CursorTypeH.CursorCircle;
                    case "mirror_pivot": return CursorTypeH.CursorPlus;
                    case "move": return CursorTypeH.CursorMove;
                    default: return CursorTypeH.CursorPencil;
                }
            }
        }
        private readonly List<string> availableParams = new() {
            "1",
            "2",
            "3",
            "4",
            "5",
            "pencil",
            "rect",
            "clear",
            "line",
            "circle",
            "ruler",
            "move",
            "mirror_pivot",
            "mirror_x",
            "mirror_y",
        };
        public override string MenuIcon => IconId.WPlanning;
        public const string ToolId = "plan";

        public override string Id => ToolId;

        public override bool IsConfigurable => true;

        private string mode = "clear";
        public readonly B<string> ModeB = new("rect");
        private int brush;

        protected override bool WallOrFloorCheck(int idx) {
            if (idx < Consts.MapMargin || idx >= S.GridSize - Consts.MapMargin) {
                return false;
            }
            return S.Map.Plan.Data[idx] != 0;
        }
        public override void AddParticles(Dictionary<int, XParticles> x) {
            var ps = GetMouseRectParticles(x);
            // The mirror axes below do not follow the cursor, they stay
            var psc = isHoverHidden ? 0 : AddModeParticles(ps.Particles, 0);

            // Axes
            var colorX = mirrorX ? Consts.PlanGuidelinesMirrorColor :
                Consts.PlanGuidelinesAccentColor;
            var colorY = mirrorY ? Consts.PlanGuidelinesMirrorColor :
                Consts.PlanGuidelinesAccentColor;

            var mirrorOffset = MirrorEven ? 0f : 0.5f;
            /*
            if (MirrorEven) {
                guideX.LineWidth = 0.12f;
                guideY.LineWidth = 0.12f;
            } else {
                guideX.LineWidth = 0.24f;
                guideY.LineWidth = 0.24f;
            }
            */
            var cam = RenderUtils.Camera;
            var lineWidth = 8f;
            guideX.LineWidth = guideY.LineWidth = lineWidth;
            if (cam != null) {
                guideX.ShowLine(
                    new Vector2(0, mirrorPointY + mirrorOffset),
                    new Vector2(Pos.GridW, mirrorPointY + mirrorOffset),
                    colorX,
                    cam);

                guideY.ShowLine(
                    new Vector2(mirrorPointX + mirrorOffset, 0),
                    new Vector2(mirrorPointX + mirrorOffset, Pos.GridH),
                    colorY,
                    cam);
            }

            if (drawRect) {
                ParticleSystem.Particle particle;
                if (mode == "mirror_pivot") {
                    particle = ParticlesSys.ParticleAt(
                        EntityUtils.ToVector2(lastHoverPos),
                        toolColor, RenderUtils.CameraThicknessMultiplier, 0.1f);
                } else {
                    var color = toolColor;
                    if (isInSpecialMode) {
                        color = Consts.PlanGuidelinesAccentColor;
                    }
                    particle = ParticlesSys.ParticleAt(lastRect.Center(),
                        color, lastRect.Extents, 0.1f);
                }
                if (ps.Particles.Length > psc) {
                    ps.Particles[psc++] = particle;
                } else {
                    psc++;
                }
            }

            ps.ShowParticles(psc, false);
        }

        private int AddModeParticles(ParticleSystem.Particle[] p, int offset) {
            if (Pos.IsOOB(dragEnd)) { return offset; }
            offset = DrawGuidelines(p, offset, dragEnd);
            if (isInBucketMode && bucketFill.Count > 0) {
                DrawBucketFill((pos) => {
                    if (p.Length > offset) {
                        p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos),
                            Consts.GreenRectColor, RenderUtils.TilePulse, 0.1f);
                    } else {
                        offset++;
                    }
                });
            } else if (isDragging) {
                offset = DrawGuidelines(p, offset, dragStart);
                if (mode == "circle" || isInCircleMode) {
                    DrawCircle((pos) => {
                        offset = DrawCell(p, offset, pos);
                    });
                } else if (mode == "line" || isInLineMode) {
                    DrawLine((pos) => {
                        offset = DrawCell(p, offset, pos);
                    });
                }
            }

            return offset;
        }

        private int DrawCell(ParticleSystem.Particle[] p, int offset, int pos) {
            if (p.Length > offset) {
                p[offset++] = ParticlesSys.ParticleAt(Pos.ToVector(pos), toolColor,
                    RenderUtils.TilePulse, 0.1f);
            } else {
                offset++;
            }
            return offset;
        }

        public override string MenuNameFor(string param) {
            return string.Format("tool.plan.{0}", param);
        }
        public override string MenuIconFor(string param) {
            switch (param) {
                case "clear" : return IconId.WPlanningClear;
                case "pencil" : return IconId.WPencil;
                case "rect" : return IconId.WSquare;
                case "ruler" : return IconId.WRuler;
                case "circle" : return IconId.WCircle;
                case "line" : return IconId.WLine;
                case "mirror_x" : return IconId.WMirrorX;
                case "mirror_y" : return IconId.WMirrorY;
                case "mirror_pivot" : return IconId.WMirrorPivot;
                case "move" : return IconId.WMove;
                default: return $"Icons/Color/Plan{param}";
            }
        }
        public override List<string> AvailableParams => availableParams;

        public readonly List<string> ModeParams = new() {
            "rect",
            "pencil",
            "line",
            "circle",
            "clear"
        };

        public readonly List<string> ExtraToolParams = new() {
            "ruler",
            "move",
            "mirror_pivot"
        };

        public Color toolColor;
        public readonly B<Color> SelectedColorB = new(default);

        public override bool Configure(string param) {
            if (param == null) { param = "rect"; }
            if (int.TryParse(param, out var colorNum)) {
                D.Warn("Changing color without setting mode: {0}", param);
                // color change, don't touch mode
                brush = colorNum;
                var col = GetColor(brush);
                toolColor = col;
                SelectedColorB.Set(toolColor);
                return true;
            }
            if (brush == 0) {
                brush = 1;
                var col = GetColor(brush);
                toolColor = col;
                SelectedColorB.Set(toolColor);
            }
            lastHoverPos = -1;
            if (param == "mirror_x") {
                mirrorX = !mirrorX;
                MirrorXB.Set(mirrorX);
                return true;
            }
            if (param == "mirror_y") {
                mirrorY = !mirrorY;
                MirrorYB.Set(mirrorY);
                return true;
            }
            mode = param;
            ModeB.Set(mode);
            if (mode == "clear" || isInDeleteMode) {
                toolColor = Consts.RedRectColor;
            } else if (mode == "ruler" || mode == "move" || isInSpecialMode) {
                toolColor = Consts.PlanGuidelinesAccentColor;
            } else if (mode == "mirror_pivot") {
                toolColor = Consts.GreenRectColor;
            }
            return true;
        }

        public static Color GetColor(int num) {
            var rgb = Tunable.IntSet($"PlanningColor{num}");
            if (rgb == null) {
                return Color.magenta;
            }
            var col = new Color32(
                (byte) rgb[0],
                (byte) rgb[1],
                (byte) rgb[2],
                (byte) Mathf.RoundToInt(255f * Tunables.PlanningToolAlpha));
            return col;
        }

        public override bool IsToggle(string param) {
            return param.StartsWith("mirror_");
        }

        public override void Activate() {
            base.Activate();
            if (guideX == null) {
                guideX = UIGuidelines.Create("GuidelineUI");
                guideY = UIGuidelines.Create("GuidelineUI");
            }
            if (mirrorPointX == 0 && mirrorPointY == 0 && MirrorPoint != 0) {
                Pos.ToXY(MirrorPoint, out mirrorPointX, out mirrorPointY);
            }
        }

        public override void Deactivate() {
            if (posText != null) {
                posText.Destroy();
                posText = null;
            }
            if (posTextDragStart != null) {
                posTextDragStart.Destroy();
                posTextDragStart = null;
            }
            tempPlan = null;
            guideX.Dispose();
            guideY.Dispose();
            base.Deactivate();
        }

        public override void OnHover(Vector2 coords) {
            if (!EntityUtils.IsWithinBounds(coords)) { return; }
            isHoverHidden = false;
            OnHoverCommon(coords, true);
            var pos = EntityUtils.ToPosSafe(coords);
            if (isDragging) {
                CheckDragModes();
                OnHoverDrag();
                return;
            } else {
                dragEnd = pos;
                CheckNonDragModes(pos);
            }
            if (pos != lastHoverPos) {
                ShowColorTile(pos, toolColor);
            }
            lastHoverPos = pos;
        }

        public override void OnDrag(Vector2 start, Vector2 end, float dragDistance) {
            if (!wasClickedDown) { return; }
            OnDragCommon(start, end, true);
            if (mode == "move" || isInSpecialMode) {
                if (lastPencilTile == Pos.Invalid) {
                    lastPencilTile = EntityUtils.ToPos(start);
                }
                if (lastPencilTile != dragEnd) {
                    var from = EntityUtils.ToVector2(lastPencilTile);
                    var to = EntityUtils.ToVector2(dragEnd);
                    MovePlan(to - from);
                    lastPencilTile = dragEnd;
                }
            } else if (mode == "circle" || isInCircleMode) {
                return;
            } else if (mode == "line" || isInLineMode) {
                drawRect = false;
                return;
            }
            if (mode == "pencil" || isInPencilMode) {
                if (lastPencilTile == Pos.Invalid) {
                    DrawAt(dragEnd);
                } else if (lastPencilTile != dragEnd) {
                    DrawLineCustom(EntityUtils.CenterOf(lastPencilTile), end, DrawAt);
                }
                S.Sig.UpdatePlan.Send();
                lastPencilTile = dragEnd;
                return;
            }
            PosRect rect = DragRect(start, end);
            HandleDragRect(rect,
                isInDeleteMode ? Consts.RedRectColor : toolColor);
        }

        public override void OnClickDown(Vector2 pos) {
            wasClickedDown = true;
            lastPencilTile = Pos.Invalid;
        }

        public override void OnClickUp(Vector2 startPos, Vector2 position,
                float dragDistance) {
            if (!wasClickedDown) { return; }
            wasClickedDown = false;
            if (isInPencilMode) {
                lastPencilTile = Pos.Invalid;
            }
            if (isInBucketMode) {
                for (int i = 0; i < bucketFill.Count; i++) {
                    int p = bucketFill[i];
                    DrawAt(p);
                }
                bucketFill.Clear();
            } else if (isDragging && (mode == "circle" || isInCircleMode)) {
                dragInfoText?.SetText(null);
                DrawCircle(DrawAt);
            } else if (isDragging && (mode == "line" || isInLineMode)) {
                DrawLine(DrawAt);
            } else if (mode == "ruler" || mode == "move" || isInSpecialMode) {
            } else if (mode == "mirror_pivot") {
                if (MirrorPoint == lastHoverPos) {
                    MirrorEven = !MirrorEven;
                } else {
                    SetMirrorPoint(lastHoverPos);
                }
            } else {
                if (!isInPencilMode) {
                    if (isDragging) {
                        foreach (var pos in dragPositions) {
                            DrawAt(pos);
                        }
                    } else {
                        var pos = EntityUtils.ToPos(position);
                        DrawAt(pos);
                        lastHoverPos = Pos.Invalid;
                    }
                }
            }
            ClearTexts();
            ClearDragPositions();
            S.Sig.UpdatePlan.Send();
            isDragging = false;
        }

        private int[] tempPlan;

        private void MovePlan(Vector2 shift) {
            int byX = Mathf.RoundToInt(shift.x);
            int byY = Mathf.RoundToInt(shift.y);
            if (byX == 0 && byY == 0) { return; }
            var gridSize = S.GridSize;
            if (tempPlan == null) {
                tempPlan = new int[gridSize];
            }
            // save the plan
            for (int i = 0; i < gridSize; i++) {
                tempPlan[i] = S.Map.Plan.Data[i];
                S.Map.Plan.Data[i] = 0;
            }
            var gridH = S.GridHeight;
            var gridW = S.GridWidth;
            // shift the plan back
            for (int i = 0; i < gridSize; i++) {
                Pos.ToXY(i, out var nx, out var ny);
                var oldX = nx;
                var oldY = ny;
                nx = AdjustAxis(nx, byX, gridH);
                ny = AdjustAxis(ny, byY, gridW);
                int newPosIdx = Pos.FromXY(nx, ny);
                if (newPosIdx < 0 || newPosIdx > gridSize - 1) {
                    D.Err("NewPos: {0}", newPosIdx);
                    D.Err("nx: {0} -> {1}. ByX: {2}", oldX, nx, byX);
                    D.Err("ny: {0} -> {1}. ByY: {2}", oldY, ny, byY);
                }
                S.Map.Plan.Data[newPosIdx] = tempPlan[i];
            }
            S.Sig.UpdatePlan.Send();
        }

        private int AdjustAxis(int a, int by, int w) {
            if (by > 0) {
                return (a + by) % w;
            } else if (by < 0) {
                if (a >= -by) {
                    return  a + by;
                } else {
                    // a = 0, w = 3 (0-2), by = -1
                    // a = 3 - (--1 - 0=1) = 3 - 1 = 2
                    return w - (-by - a);
                }
            }
            return a;
        }

        private void ClearTexts() {
            if (posTextDragStart != null) {
                posTextDragStart.SetText(null);
            }
            if (posText != null) {
                posText.SetText(null);
            }
        }

        private void DrawAt(int pos) {
            if (!EntityUtils.IsWithinMargin(pos, 1, 1)) { return; }
            if (mode == "clear" || isInDeleteMode) {
                S.Map.Plan.Data[pos] = 0;
                if (mirrorX || mirrorY) {
                    Pos.ToXY(pos, out var x, out var y);
                    var mirrorOffset = MirrorEven ? 1 : 0;
                    var mx = (2 * mirrorPointX) - x - mirrorOffset;
                    var my = (2 * mirrorPointY) - y - mirrorOffset;
                    if (mirrorX) {
                        S.Map.Plan.Data[Pos.FromXY(x, my)] = 0;
                    }
                    if (mirrorY) {
                        S.Map.Plan.Data[Pos.FromXY(mx, y)] = 0;
                    }
                    if (mirrorX && mirrorY) {
                        S.Map.Plan.Data[Pos.FromXY(mx, my)] = 0;
                    }
                }
            } else {
                if (!isDragging) {
                    // maybe we are clicking the same pos, so then toggle?
                    var oldBrush = S.Map.Plan.Data[pos];
                    if (oldBrush == brush) {
                        S.Map.Plan.Data[pos] = 0;
                        return;
                    }
                }
                S.Map.Plan.Data[pos] = brush;
                if (mirrorX || mirrorY) {
                    Pos.ToXY(pos, out var x, out var y);
                    var mirrorOffset = MirrorEven ? 1 : 0;
                    var mx = (2 * mirrorPointX) - x - mirrorOffset;
                    var my = (2 * mirrorPointY) - y - mirrorOffset;
                    if (mirrorX) {
                        S.Map.Plan.Data[Pos.FromXY(x, my)] = brush;
                    }
                    if (mirrorY) {
                        S.Map.Plan.Data[Pos.FromXY(mx, y)] = brush;
                    }
                    if (mirrorX && mirrorY) {
                        S.Map.Plan.Data[Pos.FromXY(mx, my)] = brush;
                    }
                }
            }
        }

        public override string ToString() {
            return Id;
        }
    }
}
