using System;
using Game.Rendering;
using Game.UI;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Minigames {
    // Endless stacking puzzle: ten shapes (the seven tetrominoes plus three
    // smaller/larger oddballs) fall into a well, fill rows to clear them,
    // speed ramps with progress, game over when the well overflows.
    // Left/Right use held-state auto-repeat like Breakout's paddle; rotate
    // and soft-drop stay edge-triggered like Snake's turns. Rendered with GL
    // quads.
    public sealed class MinigameStacker : Minigame {
        public const string StackerId = "Stacker";

        // Pixel space matches MinigameWindow.RtWidth/RtHeight (320x240).
        private const int Cols = 14;
        private const int Rows = 18;
        private const float CellPx = 12f;
        private const int WallT = 3;
        private const float FieldW = Cols * CellPx;
        private const float FieldH = Rows * CellPx;
        // Well is centered in the window; the preview sits in the top-left
        // corner instead of floating beside it.
        private const float FieldX = (MinigameWindow.RtWidth - FieldW) / 2f;
        private const float FieldY = (MinigameWindow.RtHeight - FieldH) / 2f;
        private const float PreviewMargin = 10f;
        private const float PreviewBoxSize = 40f;
        private const float PreviewBoxX = PreviewMargin;
        private const float PreviewBoxY = MinigameWindow.RtHeight - PreviewMargin - PreviewBoxSize;
        private const float PreviewCellPx = 8f;

        private const float StartInterval = 0.5f;
        private const float IntervalPerLine = 0.015f;
        private const float MinInterval = 0.07f;
        private const float SoftDropInterval = 0.04f;

        // Held-left/right auto-repeat (DAS): one tile on press, then a pause
        // before it starts repeating while the key stays down.
        private const float MoveRepeatDelay = 0.2f;
        private const float MoveRepeatInterval = 0.05f;

        private const int ShapeCount = 10;
        private const int MaxCells = 5;
        // Each shape lives in a fixed NxN box (bar 4, square 2, rest 3);
        // rotating maps (x, y) -> (y, N-1-x), which spins the shape around the
        // geometrical center of its box and closes after 4 steps. Cell counts
        // vary (3/4/5) - the three non-tetromino shapes break up the classic
        // set without needing any extra rotation logic, since all of them are
        // closed under the same box transform.
        private static readonly sbyte[] ShapeBoxSize = { 4, 2, 3, 3, 3, 3, 3, 2, 3, 3 };
        private static readonly sbyte[][] ShapeCells = {
            new sbyte[] { 0, 1, 1, 1, 2, 1, 3, 1 }, // long bar
            new sbyte[] { 0, 0, 1, 0, 0, 1, 1, 1 }, // square
            new sbyte[] { 0, 1, 1, 1, 2, 1, 1, 2 }, // tee
            new sbyte[] { 0, 0, 1, 0, 1, 1, 2, 1 }, // skew right
            new sbyte[] { 0, 1, 1, 1, 1, 0, 2, 0 }, // skew left
            new sbyte[] { 0, 0, 0, 1, 1, 0, 2, 0 }, // hook left
            new sbyte[] { 0, 0, 1, 0, 2, 0, 2, 1 }, // hook right
            new sbyte[] { 0, 0, 1, 0, 0, 1 }, // corner (3)
            new sbyte[] { 0, 2, 1, 2, 1, 1, 1, 0, 2, 0 }, // Z pentomino (5)
            new sbyte[] { 0, 0, 1, 0, 2, 0, 0, 1, 0, 2 }, // V pentomino (5)
        };
        private static readonly string[] ShapeColorIds = {
            "ChartBlue",
            "ChartYellow",
            "Bright",
            "Green",
            "ChartRed",
            "White",
            "ChartOrange",
            "Accent2",
            "Red",
            "Yellow",
        };

        private RenderTexture target;
        private readonly byte[] board = new byte[Cols * Rows];
        private readonly sbyte[] cells = new sbyte[MaxCells * 2];
        private readonly sbyte[] rotScratch = new sbyte[MaxCells * 2];
        private int cellCount;
        private int px;
        private int py;
        private int shapeId;
        private int nextId;
        private int totalLines;
        private float interval = StartInterval;
        private float accumulator;
        // Held left/right auto-repeat state (DAS): moveDir is the currently
        // held direction (-1/0/1), moveRepeating flips true once the first
        // repeat fires so the shorter interval applies from then on.
        private int moveDir;
        private float moveTimer;
        private bool moveRepeating;
        private bool softDropHeld;
        // Set when a soft-dropped piece locks, so the next piece ignores a
        // still-held Down until it is released and pressed again.
        private bool softDropSpent;
        private bool dirty;

        private Color colBg;
        private Color colBorder;
        private Color colField;
        private Color colDeadOverlay;
        private Color colPauseOverlay;
        private readonly Color[] shapeColors = new Color[ShapeCount];

        public override string Id => StackerId;

        public override void StartNew(RenderTexture target) {
            this.target = target;
            Array.Clear(board, 0, board.Length);
            Score = 0;
            totalLines = 0;
            interval = StartInterval;
            accumulator = 0f;
            IsDead = false;
            moveDir = 0;
            moveTimer = 0f;
            moveRepeating = false;
            softDropHeld = false;
            softDropSpent = false;
            nextId = Rng.URange(0, ShapeCount);
            Spawn();
        }

        public override void Resume(MinigameState state, RenderTexture target) {
            var s = (StackerState) state;
            this.target = target;
            s.Board.CopyTo(board, 0);
            s.Cells.CopyTo(cells, 0);
            px = s.Px;
            py = s.Py;
            shapeId = s.ShapeId;
            cellCount = ShapeCells[shapeId].Length / 2;
            nextId = s.NextId;
            totalLines = s.TotalLines;
            interval = s.Interval;
            Score = s.Score;
            accumulator = 0f;
            IsDead = false;
            moveDir = 0;
            moveTimer = 0f;
            moveRepeating = false;
            softDropHeld = false;
            softDropSpent = false;
            dirty = true;
        }

        public override MinigameState CaptureState() {
            return new StackerState {
                Board = (byte[]) board.Clone(),
                Cells = (sbyte[]) cells.Clone(),
                Px = px,
                Py = py,
                ShapeId = shapeId,
                NextId = nextId,
                TotalLines = totalLines,
                Interval = interval,
                Score = Score,
            };
        }

        // Left/Right no longer arrive here - see OnRawMove for the held-state
        // auto-repeat. Only rotate and soft-drop are edge-triggered.
        public override void OnInput(Vector2Int dir) {
            if (IsDead) { return; }
            if (dir.y > 0) {
                Rotate();
                return;
            }
            // soft drop: one immediate gravity step, gravity timer reset
            if (dir.y < 0 && Fits(cells, px, py - 1)) {
                py--;
                accumulator = 0f;
                dirty = true;
            }
        }

        public override void OnRawMove(Vector2 raw) {
            softDropHeld = raw.y < -0.5f;
            if (!softDropHeld) {
                softDropSpent = false;
            }
            var dir = raw.x > 0.5f ? 1 : raw.x < -0.5f ? -1 : 0;
            if (dir == moveDir) { return; }
            moveDir = dir;
            moveTimer = 0f;
            moveRepeating = false;
            if (dir != 0) {
                TryMove(dir);
            }
        }

        public override bool Step(float unscaledDt) {
            StepMove(unscaledDt);
            accumulator += unscaledDt;
            var changed = false;
            while (!IsDead) {
                var itv = softDropHeld && !softDropSpent ? SoftDropInterval : interval;
                if (accumulator < itv) { break; }
                accumulator -= itv;
                GravityStep();
                changed = true;
            }
            if (dirty) {
                dirty = false;
                changed = true;
            }
            return changed;
        }

        // On press: dir just changed (handled in OnRawMove) - move once. While
        // held: accumulate dt here and repeat every MoveRepeatInterval once
        // MoveRepeatDelay has elapsed, carrying the remainder for an exact rate.
        private void StepMove(float dt) {
            if (moveDir == 0) { return; }
            moveTimer += dt;
            var threshold = moveRepeating ? MoveRepeatInterval : MoveRepeatDelay;
            while (moveTimer >= threshold) {
                moveTimer -= threshold;
                moveRepeating = true;
                threshold = MoveRepeatInterval;
                TryMove(moveDir);
            }
        }

        // OnRawMove keeps firing after death, so this must stay a safe no-op.
        private bool TryMove(int dx) {
            if (IsDead || !Fits(cells, px + dx, py)) { return false; }
            px += dx;
            dirty = true;
            return true;
        }

        public override void Render() {
            if (target == null) { return; }
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, MinigameWindow.RtWidth, 0, MinigameWindow.RtHeight);
            GL.Clear(false, true, colBg);
            GL.Begin(GL.QUADS);
            RenderUtils.LineMaterial.SetPass(0);

            GL.Color(colBorder);
            DrawBorder();
            // frame around the centered well - with no separator to lean on,
            // this is what marks the play area as the focal element
            DrawRectFrame(FieldX - 1f, FieldY - 1f, FieldW + 2f, FieldH + 2f);

            // well interior
            GL.Color(colField);
            DrawRect(FieldX, FieldY, FieldX + FieldW, FieldY + FieldH);

            // settled blocks
            for (int r = 0; r < Rows; r++) {
                for (int c = 0; c < Cols; c++) {
                    var v = board[r * Cols + c];
                    if (v == 0) { continue; }
                    GL.Color(shapeColors[v - 1]);
                    DrawBlock(FieldX + c * CellPx, FieldY + r * CellPx);
                }
            }

            // falling piece
            if (!IsDead) {
                GL.Color(shapeColors[shapeId]);
                for (int i = 0; i < cellCount; i++) {
                    DrawBlock(FieldX + (px + cells[i * 2]) * CellPx,
                        FieldY + (py + cells[i * 2 + 1]) * CellPx);
                }
            }

            // next-piece slot in the top-left corner
            GL.Color(colBorder);
            DrawRectFrame(PreviewBoxX, PreviewBoxY, PreviewBoxSize, PreviewBoxSize);
            var baseCells = ShapeCells[nextId];
            var nextCount = baseCells.Length / 2;
            CellBounds(baseCells, nextCount, out var minX, out var minY, out var maxX, out var maxY);
            var spanW = (maxX - minX + 1) * PreviewCellPx;
            var spanH = (maxY - minY + 1) * PreviewCellPx;
            var offX = PreviewBoxX + (PreviewBoxSize - spanW) / 2f - minX * PreviewCellPx;
            var offY = PreviewBoxY + (PreviewBoxSize - spanH) / 2f - minY * PreviewCellPx;
            GL.Color(shapeColors[nextId]);
            for (int i = 0; i < nextCount; i++) {
                var cx = offX + baseCells[i * 2] * PreviewCellPx;
                var cy = offY + baseCells[i * 2 + 1] * PreviewCellPx;
                DrawRect(cx + 1f, cy + 1f, cx + PreviewCellPx - 1f,
                    cy + PreviewCellPx - 1f);
            }

            if (IsDead) {
                GL.Color(colDeadOverlay);
                DrawRect(0f, 0f, MinigameWindow.RtWidth, MinigameWindow.RtHeight);
            } else if (IsPaused) {
                GL.Color(colPauseOverlay);
                DrawRect(0f, 0f, MinigameWindow.RtWidth, MinigameWindow.RtHeight);
            }

            GL.End();
            GL.PopMatrix();
            RenderTexture.active = null;
        }

        public override void OnThemeColorsChanged() {
            var t = The.UITheme;
            colBg = t.GetColor("MiniChartBackground", this);
            colBorder = t.GetColor("White", 0.4f, this);
            colField = t.GetColor("Black", 0.35f, this);
            colDeadOverlay = t.GetColor("AlertRed", 0.35f, this);
            colPauseOverlay = t.GetColor("Black", 0.4f, this);
            for (int i = 0; i < ShapeCount; i++) {
                shapeColors[i] = t.GetColor(ShapeColorIds[i], this);
            }
        }

        private void GravityStep() {
            if (!Fits(cells, px, py - 1)) {
                Lock();
                return;
            }
            py--;
            dirty = true;
        }

        private void Rotate() {
            // 90 degrees clockwise around the center of the shape's NxN box:
            // (x, y) -> (y, N-1-x). Closed integer cycle, no pivot drift.
            // Computed once; horizontal kicks then just retarget px. Kicks
            // grow up to n-1 tiles either way, which covers the worst case
            // (e.g. the 4-wide bar needs a 2-tile kick to flip flat against a
            // wall) - a fixed +-1 silently refused to rotate there.
            var n = ShapeBoxSize[shapeId];
            for (int i = 0; i < cellCount; i++) {
                rotScratch[i * 2] = cells[i * 2 + 1];
                rotScratch[i * 2 + 1] = (sbyte) (n - 1 - cells[i * 2]);
            }
            for (int mag = 0; mag < n; mag++) {
                var signs = mag == 0 ? 1 : 2;
                for (int s = 0; s < signs; s++) {
                    var dx = mag == 0 ? 0 : s == 0 ? -mag : mag;
                    var tx = px + dx;
                    if (!Fits(rotScratch, tx, py)) { continue; }
                    for (int i = 0; i < cellCount * 2; i++) {
                        cells[i] = rotScratch[i];
                    }
                    px = tx;
                    dirty = true;
                    return;
                }
            }
        }

        private void Lock() {
            softDropSpent = true;
            for (int i = 0; i < cellCount; i++) {
                var bx = px + cells[i * 2];
                var by = py + cells[i * 2 + 1];
                board[by * Cols + bx] = (byte) (shapeId + 1);
            }
            UISounds.Play("FX/MinigameHit01", 0.5f);
            var cleared = ClearLines();
            if (cleared > 0) {
                Score += cleared switch {
                    1 => 100,
                    2 => 300,
                    3 => 500,
                    _ => 800,
                };
                UISounds.Play(cleared >= 4 ? "FX/MinigameWin01" : "FX/MinigameLose01", 0.6f);
            }
            interval = Maths.Max(MinInterval, StartInterval - totalLines * IntervalPerLine);
            Spawn();
        }

        // Compacts surviving rows toward row 0 (the bottom); full rows vanish
        // and everything above falls down.
        private int ClearLines() {
            var cleared = 0;
            var write = 0;
            for (int read = 0; read < Rows; read++) {
                var full = true;
                for (int c = 0; c < Cols; c++) {
                    if (board[read * Cols + c] == 0) {
                        full = false;
                        break;
                    }
                }
                if (full) {
                    cleared++;
                    continue;
                }
                if (write != read) {
                    for (int c = 0; c < Cols; c++) {
                        board[write * Cols + c] = board[read * Cols + c];
                    }
                }
                write++;
            }
            for (; write < Rows; write++) {
                for (int c = 0; c < Cols; c++) {
                    board[write * Cols + c] = 0;
                }
            }
            if (cleared > 0) {
                totalLines += cleared;
                dirty = true;
            }
            return cleared;
        }

        private void Spawn() {
            shapeId = nextId;
            var baseCells = ShapeCells[shapeId];
            cellCount = baseCells.Length / 2;
            for (int i = 0; i < cellCount * 2; i++) {
                cells[i] = baseCells[i];
            }
            px = (Cols - ShapeBoxSize[shapeId]) / 2;
            py = Rows - ShapeBoxSize[shapeId];
            nextId = Rng.URange(0, ShapeCount);
            accumulator = 0f;
            if (!Fits(cells, px, py)) {
                // the well overflowed - nothing fits anymore
                UISounds.Play("FX/MinigameExplode01", 0.75f);
                Die();
                return;
            }
            dirty = true;
        }

        private void Die() {
            IsDead = true;
            dirty = true;
        }

        private bool Fits(sbyte[] checkCells, int checkX, int checkY) {
            for (int i = 0; i < cellCount; i++) {
                var bx = checkX + checkCells[i * 2];
                var by = checkY + checkCells[i * 2 + 1];
                if (bx < 0 || bx >= Cols || by < 0 || by >= Rows) { return false; }
                if (board[by * Cols + bx] != 0) { return false; }
            }
            return true;
        }

        // (minX, minY, maxX, maxY) of a normalized cell set - used to center
        // the next-piece preview on its actual cells rather than its NxN
        // rotation box, which would leave most shapes looking off-center.
        private static void CellBounds(sbyte[] c, int count,
                out int minX, out int minY, out int maxX, out int maxY) {
            minX = sbyte.MaxValue;
            minY = sbyte.MaxValue;
            maxX = 0;
            maxY = 0;
            for (int i = 0; i < count; i++) {
                var x = c[i * 2];
                var y = c[i * 2 + 1];
                if (x < minX) { minX = x; }
                if (y < minY) { minY = y; }
                if (x > maxX) { maxX = x; }
                if (y > maxY) { maxY = y; }
            }
        }

        private void DrawBorder() {
            RenderUtils.DrawQuadLine(new Vector3(1f, 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, 1f, 0f), WallT);
            RenderUtils.DrawQuadLine(new Vector3(1f, MinigameWindow.RtHeight - 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, MinigameWindow.RtHeight - 1f, 0f),
                WallT);
            RenderUtils.DrawQuadLine(new Vector3(1f, 1f, 0f),
                new Vector3(1f, MinigameWindow.RtHeight - 1f, 0f), WallT);
            RenderUtils.DrawQuadLine(new Vector3(MinigameWindow.RtWidth - 1f, 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, MinigameWindow.RtHeight - 1f, 0f),
                WallT);
        }

        private static void DrawBlock(float x, float y) {
            DrawRect(x + 1f, y + 1f, x + CellPx - 1f, y + CellPx - 1f);
        }

        private static void DrawRectFrame(float x, float y, float w, float h) {
            DrawRect(x, y, x + w, y + 1f);
            DrawRect(x, y + h - 1f, x + w, y + h);
            DrawRect(x, y + 1f, x + 1f, y + h - 1f);
            DrawRect(x + w - 1f, y + 1f, x + w, y + h - 1f);
        }

        private static void DrawRect(float x0, float y0, float x1, float y1) {
            GL.Vertex3(x0, y0, 0f);
            GL.Vertex3(x0, y1, 0f);
            GL.Vertex3(x1, y1, 0f);
            GL.Vertex3(x1, y0, 0f);
        }

        private sealed class StackerState : MinigameState {
            public byte[] Board;
            public sbyte[] Cells;
            public int Px;
            public int Py;
            public int ShapeId;
            public int NextId;
            public int TotalLines;
            public float Interval;
            public int Score;
        }
    }
}
