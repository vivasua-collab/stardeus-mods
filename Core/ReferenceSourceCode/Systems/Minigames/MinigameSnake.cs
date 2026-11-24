using System;
using Game.Rendering;
using Game.UI;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Minigames {
    // Classic Snake rendered with GL quads into a shared RenderTexture.
    // Steady state is allocation-free: fixed occupancy array, ring buffer body,
    // cached theme colors, all vertex math inline.
    public sealed class MinigameSnake : Minigame {
        public const string SnakeId = "Snake";

        // GridW * CellPx == MinigameWindow.RtWidth, GridH * CellPx ==
        // MinigameWindow.RtHeight (4:3 board, square cells).
        private const int GridW = 32;
        private const int GridH = 24;
        private const int CellPx = 10;
        private const float StepInterval = 0.28f;
        private const float MinStepInterval = 0.06f;
        private const float IntervalDecrement = 0.004f;
        private const float CellInset = 1f;
        private const float WallThickness = 3f;

        private RenderTexture target;
        private readonly byte[] cells = new byte[GridW * GridH];
        private readonly int[] body = new int[GridW * GridH];
        private int headIdx;
        private int tailIdx;
        private int count;
        private Vector2Int dir;
        private Vector2Int pendingDir;
        private int foodCell = -1;
        private float interval = StepInterval;
        private float accumulator;
        private bool dirty;

        private Color colBg;
        private Color colBorder;
        private Color colBody;
        private Color colHead;
        private Color colFood;
        private Color colDeadOverlay;
        private Color colPauseOverlay;

        public override string Id => SnakeId;

        public override void StartNew(RenderTexture target) {
            this.target = target;
            Array.Clear(cells, 0, cells.Length);
            count = 0;
            tailIdx = 0;
            headIdx = -1;
            dir = Vector2Int.right;
            pendingDir = dir;
            interval = StepInterval;
            accumulator = 0f;
            Score = 0;
            IsDead = false;
            dirty = true;
            var cy = GridH / 2;
            var cx = GridW / 2 - 1;
            Push(cx - 1, cy);
            Push(cx, cy);
            Push(cx + 1, cy);
            PlaceFood();
        }

        public override void Resume(MinigameState state, RenderTexture target) {
            var s = (SnakeState) state;
            this.target = target;
            s.Cells.CopyTo(cells, 0);
            s.Body.CopyTo(body, 0);
            headIdx = s.HeadIdx;
            tailIdx = s.TailIdx;
            count = s.Count;
            dir = s.Dir;
            pendingDir = s.PendingDir;
            foodCell = s.FoodCell;
            interval = s.Interval;
            accumulator = 0f;
            Score = s.Score;
            IsDead = false;
            dirty = true;
        }

        public override MinigameState CaptureState() {
            return new SnakeState {
                Cells = (byte[]) cells.Clone(),
                Body = (int[]) body.Clone(),
                HeadIdx = headIdx,
                TailIdx = tailIdx,
                Count = count,
                Dir = dir,
                PendingDir = pendingDir,
                FoodCell = foodCell,
                Interval = interval,
                Score = Score,
            };
        }

        public override void OnInput(Vector2Int d) {
            if (d == Vector2Int.zero) { return; }
            if (d == dir || d == pendingDir) { return; }
            // Reject 180-degree turns against both the applied direction and
            // the buffered one (pressing two keys within one step otherwise
            // allows reversing through the neck).
            if (d == -dir || d == -pendingDir) { return; }
            pendingDir = d;
        }

        public override bool Step(float unscaledDt) {
            accumulator += unscaledDt;
            var changed = false;
            while (accumulator >= interval) {
                accumulator -= interval;
                changed = true;
                Advance();
                if (IsDead) { break; }
            }
            if (dirty) {
                dirty = false;
                changed = true;
            }
            return changed;
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
            if (foodCell >= 0) {
                GL.Color(colFood);
                DrawCell(foodCell);
            }
            for (int i = 0; i < count; i++) {
                var cell = body[(tailIdx + i) % body.Length];
                GL.Color(i == count - 1 ? colHead : colBody);
                DrawCell(cell);
            }
            if (IsDead) {
                GL.Color(colDeadOverlay);
                DrawQuad(0f, 0f, MinigameWindow.RtWidth, MinigameWindow.RtHeight);
            } else if (IsPaused) {
                // controls do not work while another window is on top
                GL.Color(colPauseOverlay);
                DrawQuad(0f, 0f, MinigameWindow.RtWidth, MinigameWindow.RtHeight);
            }

            GL.End();
            GL.PopMatrix();
            RenderTexture.active = null;
        }

        public override void OnThemeColorsChanged() {
            var t = The.UITheme;
            colBg = t.GetColor("MiniChartBackground", this);
            colBorder = t.GetColor("White", 0.4f, this);
            colBody = t.GetColor("Green", this);
            colHead = t.GetColor("White", this);
            colFood = t.GetColor("Red", this);
            colDeadOverlay = t.GetColor("AlertRed", 0.35f, this);
            colPauseOverlay = t.GetColor("Black", 0.4f, this);
        }

        private void Advance() {
            if (pendingDir != dir) { dir = pendingDir; }
            var headCell = body[headIdx];
            var hx = headCell % GridW + dir.x;
            var hy = headCell / GridW + dir.y;
            if (hx < 0 || hx >= GridW || hy < 0 || hy >= GridH) {
                UISounds.Play("FX/MinigameExplode01", 0.75f);
                Die();
                return;
            }
            var next = hy * GridW + hx;
            var grow = next == foodCell;
            if (!grow) {
                // Vacate the tail first so following it closely is legal.
                cells[body[tailIdx]] = 0;
                tailIdx = (tailIdx + 1) % body.Length;
                count--;
            }
            if (cells[next] != 0) {
                UISounds.Play("FX/MinigameExplode01", 0.75f);
                Die();
                return;
            }
            headIdx = (headIdx + 1) % body.Length;
            body[headIdx] = next;
            cells[next] = 1;
            count++;
            if (grow) {
                Score++;
                interval = Maths.Max(MinStepInterval, interval - IntervalDecrement);
                PlaceFood();
                UISounds.Play("FX/MinigamePickup01", 0.5f);
            }
        }

        private void Die() {
            IsDead = true;
            dirty = true;
        }

        // Walks free cells instead of retrying random rolls, so it stays
        // bounded and cannot spin as the board fills. Rng.URange routes through
        // Rng.Unseeded, keeping the simulation's seeded stream untouched.
        private void PlaceFood() {
            var freeCount = 0;
            for (int i = 0; i < cells.Length; i++) {
                if (cells[i] == 0) { freeCount++; }
            }
            if (freeCount == 0) {
                foodCell = -1;
                // board cleared - the win condition ends the run
                UISounds.Play("FX/MinigameWin01");
                Die();
                return;
            }
            var k = Rng.URange(0, freeCount);
            for (int i = 0; i < cells.Length; i++) {
                if (cells[i] != 0) { continue; }
                if (k == 0) {
                    foodCell = i;
                    return;
                }
                k--;
            }
        }

        private void Push(int x, int y) {
            headIdx = (headIdx + 1) % body.Length;
            body[headIdx] = y * GridW + x;
            cells[body[headIdx]] = 1;
            count++;
            if (count == 1) { tailIdx = headIdx; }
        }

        private void DrawBorder() {
            RenderUtils.DrawQuadLine(new Vector3(1f, 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, 1f, 0f), WallThickness);
            RenderUtils.DrawQuadLine(new Vector3(1f, MinigameWindow.RtHeight - 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, MinigameWindow.RtHeight - 1f, 0f),
                WallThickness);
            RenderUtils.DrawQuadLine(new Vector3(1f, 1f, 0f),
                new Vector3(1f, MinigameWindow.RtHeight - 1f, 0f), WallThickness);
            RenderUtils.DrawQuadLine(new Vector3(MinigameWindow.RtWidth - 1f, 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, MinigameWindow.RtHeight - 1f, 0f),
                WallThickness);
        }

        private void DrawCell(int cell) {
            var x = cell % GridW * CellPx;
            var y = cell / GridW * CellPx;
            DrawQuad(x + CellInset, y + CellInset,
                x + CellPx - CellInset, y + CellPx - CellInset);
        }

        private static void DrawQuad(float x0, float y0, float x1, float y1) {
            GL.Vertex3(x0, y0, 0f);
            GL.Vertex3(x0, y1, 0f);
            GL.Vertex3(x1, y1, 0f);
            GL.Vertex3(x1, y0, 0f);
        }

        private sealed class SnakeState : MinigameState {
            public byte[] Cells;
            public int[] Body;
            public int HeadIdx;
            public int TailIdx;
            public int Count;
            public int Score;
            public Vector2Int Dir;
            public Vector2Int PendingDir;
            public int FoodCell;
            public float Interval;
        }
    }
}
