using System;
using Game.Rendering;
using Game.UI;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Minigames {
    // Classic 2048 sliding puzzle: 4x4 board, directional inputs slide and
    // merge tiles, one new tile (2, rarely 4) spawns per successful move.
    // Reaching 2048 ends the run as a win, a locked board ends it as a loss.
    // Purely input-driven: no time simulation, Step only relays the dirty
    // flag. Numbers are drawn with MiniFont3x5 quads.
    public sealed class Minigame2048 : Minigame {
        public const string Game2048Id = "2048";

        // Pixel space matches MinigameWindow.RtWidth/RtHeight (320x240).
        private const int Cols = 4;
        private const int Rows = 4;
        private const float Cell = 40f;
        private const float Gap = 4f;
        private const float Pad = 4f;
        private const float CellStep = Cell + Gap;
        private const float BoardSide = Cols * Cell + (Cols + 1) * Gap;
        private const float BoardX = (MinigameWindow.RtWidth - BoardSide) / 2f;
        private const float BoardY = (MinigameWindow.RtHeight - BoardSide) / 2f;
        private const float TileInset = 2f;
        private const float FontScale = 2f;
        private const int WinTile = 2048;

        // One tier per exponent (2..2048 = exactly 11 tiers, the run ends at
        // 2048), hottest colors for the highest tiles.
        private static readonly string[] TileColorIds = {
            "White",
            "ChartYellow",
            "ChartBlue",
            "ChartRed",
            "Green",
            "ChartOrange",
            "Accent2",
            "Yellow",
            "Red",
            "Bright",
            "Accent",
        };

        private RenderTexture target;
        private readonly int[] board = new int[Cols * Rows];
        private readonly Color[] tileColors = new Color[TileColorIds.Length];
        private bool mergedThisMove;
        private bool wonThisMove;
        private bool dirty;

        private Color colBg;
        private Color colBorder;
        private Color colField;
        private Color colDigit;
        private Color colDeadOverlay;
        private Color colPauseOverlay;

        public override string Id => Game2048Id;

        public override void StartNew(RenderTexture target) {
            this.target = target;
            Array.Clear(board, 0, board.Length);
            Score = 0;
            IsDead = false;
            dirty = true;
            Spawn();
            Spawn();
        }

        public override void Resume(MinigameState state, RenderTexture target) {
            var s = (Game2048State) state;
            this.target = target;
            s.Board.CopyTo(board, 0);
            Score = s.Score;
            IsDead = false;
            dirty = true;
        }

        public override MinigameState CaptureState() {
            return new Game2048State {
                Board = (int[]) board.Clone(),
                Score = Score,
            };
        }

        public override void OnInput(Vector2Int dir) {
            if (IsDead) { return; }
            mergedThisMove = false;
            wonThisMove = false;
            if (!Move(dir.x, dir.y)) { return; }
            dirty = true;
            UISounds.Play(mergedThisMove ? "FX/MinigameHit01" : "FX/MinigameSlap01",
                mergedThisMove ? 0.5f : 0.25f);
            if (wonThisMove) {
                UISounds.Play("FX/MinigameWin01", 0.75f);
                Die();
                return;
            }
            Spawn();
            if (!HasMoves()) {
                UISounds.Play("FX/MinigameExplode01", 0.75f);
                Die();
            }
        }

        // No time simulation: moves happen in OnInput, Step only reports the
        // dirty flag so the render + title refresh trigger.
        public override bool Step(float unscaledDt) {
            if (!dirty) { return false; }
            dirty = false;
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
            DrawOuterBorder();

            // board frame and empty slot backgrounds
            GL.Color(colField);
            DrawRectFrame(BoardX - 1f, BoardY - 1f, BoardSide + 2f, BoardSide + 2f);
            for (var r = 0; r < Rows; r++) {
                for (var c = 0; c < Cols; c++) {
                    if (board[r * Cols + c] != 0) { continue; }
                    CellRect(c, r, out var x0, out var y0, out var x1, out var y1);
                    DrawRect(x0, y0, x1, y1);
                }
            }

            // tiles with their values
            for (var r = 0; r < Rows; r++) {
                for (var c = 0; c < Cols; c++) {
                    var v = board[r * Cols + c];
                    if (v == 0) { continue; }
                    CellRect(c, r, out var x0, out var y0, out var x1, out var y1);
                    GL.Color(TileColor(v));
                    DrawRect(x0 + TileInset, y0 + TileInset, x1 - TileInset, y1 - TileInset);
                    GL.Color(colDigit);
                    var digits = 1;
                    for (var rest = v; rest >= 10; rest /= 10) { digits++; }
                    var w = MiniFont3x5.NumberWidth(digits, FontScale);
                    var h = MiniFont3x5.GlyphH * FontScale;
                    MiniFont3x5.DrawNumber(v, (x0 + x1 - w) / 2f, (y0 + y1 - h) / 2f, FontScale);
                }
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
            colDigit = t.GetColor("Black", this);
            colDeadOverlay = t.GetColor("AlertRed", 0.35f, this);
            colPauseOverlay = t.GetColor("Black", 0.4f, this);
            for (var i = 0; i < TileColorIds.Length; i++) {
                tileColors[i] = t.GetColor(TileColorIds[i], this);
            }
        }

        // Slides every line toward the given direction, merging each pair of
        // equal tiles once per move. (line, pos) mapping is direction aware:
        // pos counts from the wall tiles slide toward, so a single walk order
        // covers all four directions.
        private bool Move(int dx, int dy) {
            var changed = false;
            var lines = dy != 0 ? Rows : Cols;
            for (var line = 0; line < lines; line++) {
                var write = 0;
                var prev = 0;
                // the previous placed tile already merged this move and is
                // locked until the next different value passes by
                var prevLocked = false;
                for (var pos = 0; pos < 4; pos++) {
                    var idx = Idx(line, pos, dx, dy);
                    var v = board[idx];
                    if (v == 0) { continue; }
                    if (!prevLocked && prev == v) {
                        write--;
                        prev += v;
                        board[Idx(line, write, dx, dy)] = prev;
                        board[idx] = 0;
                        Score += prev;
                        prevLocked = true;
                        mergedThisMove = true;
                        changed = true;
                        if (prev >= WinTile) { wonThisMove = true; }
                    } else {
                        prev = v;
                        prevLocked = false;
                        if (write != pos) {
                            changed = true;
                            board[Idx(line, write, dx, dy)] = v;
                            board[idx] = 0;
                        }
                    }
                    write++;
                }
            }
            return changed;
        }

        // Board index for movement coordinate (line, pos): pos 0 is the cell
        // at the target wall.
        private static int Idx(int line, int pos, int dx, int dy) {
            if (dx > 0) { return line * Cols + (Cols - 1 - pos); }
            if (dx < 0) { return line * Cols + pos; }
            return dy > 0 ? (Rows - 1 - pos) * Cols + line : pos * Cols + line;
        }

        private void Spawn() {
            var empties = 0;
            for (var i = 0; i < board.Length; i++) {
                if (board[i] == 0) { empties++; }
            }
            if (empties == 0) { return; }
            var pick = Rng.URange(0, empties);
            for (var i = 0; i < board.Length; i++) {
                if (board[i] != 0) { continue; }
                if (pick-- != 0) { continue; }
                board[i] = Rng.URange(0, 10) == 0 ? 4 : 2;
                return;
            }
        }

        private bool HasMoves() {
            for (var r = 0; r < Rows; r++) {
                for (var c = 0; c < Cols; c++) {
                    var v = board[r * Cols + c];
                    if (v == 0) { return true; }
                    if (c < Cols - 1 && board[r * Cols + c + 1] == v) { return true; }
                    if (r < Rows - 1 && board[(r + 1) * Cols + c] == v) { return true; }
                }
            }
            return false;
        }

        private void Die() {
            IsDead = true;
            dirty = true;
        }

        private Color TileColor(int v) {
            var e = 0;
            while (v > 1) {
                v >>= 1;
                e++;
            }
            return tileColors[(e - 1) % tileColors.Length];
        }

        private static void CellRect(int c, int r,
                out float x0, out float y0, out float x1, out float y1) {
            x0 = BoardX + Pad + c * CellStep;
            y0 = BoardY + Pad + r * CellStep;
            x1 = x0 + Cell;
            y1 = y0 + Cell;
        }

        private void DrawOuterBorder() {
            RenderUtils.DrawQuadLine(new Vector3(1f, 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, 1f, 0f), 3f);
            RenderUtils.DrawQuadLine(new Vector3(1f, MinigameWindow.RtHeight - 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, MinigameWindow.RtHeight - 1f, 0f), 3f);
            RenderUtils.DrawQuadLine(new Vector3(1f, 1f, 0f),
                new Vector3(1f, MinigameWindow.RtHeight - 1f, 0f), 3f);
            RenderUtils.DrawQuadLine(new Vector3(MinigameWindow.RtWidth - 1f, 1f, 0f),
                new Vector3(MinigameWindow.RtWidth - 1f, MinigameWindow.RtHeight - 1f, 0f), 3f);
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

        private sealed class Game2048State : MinigameState {
            public int[] Board;
            public int Score;
        }
    }
}
