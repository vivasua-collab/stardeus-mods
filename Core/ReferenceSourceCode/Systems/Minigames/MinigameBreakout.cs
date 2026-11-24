using Game.Rendering;
using Game.UI;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Minigames {
    // Classic Breakout rendered with GL quads into a shared RenderTexture.
    // Continuous ball physics integrated in bounded substeps; steady state is
    // allocation-free (fixed hp array, scalar state, cached theme colors).
    public sealed class MinigameBreakout : Minigame {
        public const string BreakoutId = "Breakout";

        // Pixel space matches MinigameWindow.RtWidth/RtHeight (320x240).
        private const int Cols = 10;
        private const int Rows = 6;
        private const int WallT = 3;
        private const int SidePad = 7;
        private const float PlayLeft = WallT + SidePad;
        private const float CellW = 30f;
        private const float GapInset = 1f;
        private const float BrickW = CellW - 2f * GapInset;
        private const float BrickH = 9f;
        private const float CellH = 11f;
        private const float FieldTop = MinigameWindow.RtHeight - WallT - 8f;
        private const float PaddleW = 48f;
        private const float PaddleHalfW = PaddleW / 2f;
        private const float PaddleH = 5f;
        private const float PaddleY = 14f;
        private const float PaddleSpeed = 160f;
        private const float BallSize = 5f;
        private const float BallHalf = BallSize / 2f;
        private const float SpeedBase = 150f;
        private const float LevelSpeedMul = 1.15f;
        private const float MaxSpeedFactor = 2f;
        // Under half of the thinnest collider dimension, so a single substep
        // can never tunnel through a paddle, brick or wall.
        private const float MaxSubstepPx = 4f;
        private const float MaxDeflectRad = 1.0472f;
        private const float PipSize = 4f;
        private const float PipSpacing = 6f;

        private RenderTexture target;
        private readonly byte[] hp = new byte[Cols * Rows];
        private float paddleX;
        private float ballX;
        private float ballY;
        private float velX;
        private float velY;
        private float speed;
        private float moveDir;
        private int lives;
        private int level;
        private int bricksLeft;
        private bool dirty;

        private Color colBg;
        private Color colBorder;
        private Color colTier3;
        private Color colTier2;
        private Color colTier1;
        private Color colPaddle;
        private Color colBall;
        private Color colDeadOverlay;
        private Color colPauseOverlay;

        public override string Id => BreakoutId;

        public override void StartNew(RenderTexture target) {
            this.target = target;
            paddleX = MinigameWindow.RtWidth / 2f;
            lives = 3;
            level = 1;
            Score = 0;
            IsDead = false;
            moveDir = 0f;
            speed = SpeedFor();
            BuildBricks();
            ResetBall();
            dirty = true;
        }

        public override void Resume(MinigameState state, RenderTexture target) {
            var s = (BreakoutState) state;
            this.target = target;
            s.Hp.CopyTo(hp, 0);
            paddleX = s.PaddleX;
            ballX = s.BallX;
            ballY = s.BallY;
            velX = s.VelX;
            velY = s.VelY;
            speed = s.Speed;
            lives = s.Lives;
            level = s.Level;
            bricksLeft = s.BricksLeft;
            Score = s.Score;
            moveDir = 0f;
            IsDead = false;
            dirty = true;
        }

        public override MinigameState CaptureState() {
            return new BreakoutState {
                Hp = (byte[]) hp.Clone(),
                PaddleX = paddleX,
                BallX = ballX,
                BallY = ballY,
                VelX = velX,
                VelY = velY,
                Speed = speed,
                Lives = lives,
                Level = level,
                BricksLeft = bricksLeft,
                Score = Score,
            };
        }

        public override void OnInput(Vector2Int dir) {
            // paddle is driven via OnRawMove; discrete turns are unused here
        }

        public override void OnRawMove(Vector2 raw) {
            if (raw.x > 0.5f) {
                moveDir = 1f;
            } else if (raw.x < -0.5f) {
                moveDir = -1f;
            } else {
                moveDir = 0f;
            }
        }

        public override bool Step(float unscaledDt) {
            var changed = false;
            if (ApplyPaddleMove(unscaledDt)) {
                changed = true;
            }
            var left = unscaledDt;
            while (left > 0f && !IsDead) {
                var dtSub = Maths.Min(left, MaxSubstepPx / speed);
                MoveBall(dtSub);
                left -= dtSub;
                changed = true;
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

            for (int r = 0; r < Rows; r++) {
                var brickTop = FieldTop - r * CellH;
                var y0 = brickTop - BrickH;
                for (int c = 0; c < Cols; c++) {
                    var h = hp[r * Cols + c];
                    if (h == 0) { continue; }
                    // color derives from current hp: chips recolor the brick
                    GL.Color(h >= 3 ? colTier3 : h == 2 ? colTier2 : colTier1);
                    var x0 = PlayLeft + c * CellW + GapInset;
                    DrawRect(x0, y0, x0 + BrickW, brickTop);
                }
            }

            GL.Color(colPaddle);
            DrawRect(paddleX - PaddleHalfW, PaddleY,
                paddleX + PaddleHalfW, PaddleY + PaddleH);

            GL.Color(colBall);
            DrawRect(ballX - BallHalf, ballY - BallHalf,
                ballX + BallHalf, ballY + BallHalf);

            GL.Color(colPaddle);
            var pipRight = MinigameWindow.RtWidth - WallT - PipSize;
            var pipTop = MinigameWindow.RtHeight - WallT - PipSize;
            for (int i = 0; i < lives; i++) {
                var x1 = pipRight - i * PipSpacing;
                DrawRect(x1 - PipSize, pipTop - PipSize, x1, pipTop);
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
            colTier3 = t.GetColor("ChartBlue", this);
            colTier2 = t.GetColor("ChartYellow", this);
            colTier1 = t.GetColor("Green", this);
            colPaddle = t.GetColor("White", this);
            colBall = t.GetColor("Red", this);
            colDeadOverlay = t.GetColor("AlertRed", 0.35f, this);
            colPauseOverlay = t.GetColor("Black", 0.4f, this);
        }

        private bool ApplyPaddleMove(float dt) {
            if (moveDir == 0f) { return false; }
            paddleX = Mathf.Clamp(paddleX + moveDir * PaddleSpeed * dt,
                WallT + PaddleHalfW, MinigameWindow.RtWidth - WallT - PaddleHalfW);
            return true;
        }

        private void MoveBall(float dt) {
            ballX += velX * dt;
            ballY += velY * dt;

            var minX = WallT + BallHalf;
            var maxX = MinigameWindow.RtWidth - WallT - BallHalf;
            if (ballX < minX && velX < 0f) {
                ballX = minX;
                velX = -velX;
                UISounds.Play("FX/MinigameBump01", 0.5f);
            } else if (ballX > maxX && velX > 0f) {
                ballX = maxX;
                velX = -velX;
                UISounds.Play("FX/MinigameBump01", 0.5f);
            }
            var maxY = MinigameWindow.RtHeight - WallT - BallHalf;
            if (ballY > maxY && velY > 0f) {
                ballY = maxY;
                velY = -velY;
                UISounds.Play("FX/MinigameBump01", 0.5f);
            }

            BounceOffPaddle();
            CollideBricks();
            if (ballY < WallT) {
                LoseLife();
            }
        }

        private void BounceOffPaddle() {
            if (velY >= 0f) { return; }
            var paddleTop = PaddleY + PaddleH;
            if (ballY - BallHalf > paddleTop || ballY < PaddleY) { return; }
            if (Mathf.Abs(ballX - paddleX) > PaddleHalfW + BallHalf) { return; }
            ballY = paddleTop + BallHalf;
            var t = (ballX - paddleX) / PaddleHalfW;
            if (t < -1f) { t = -1f; } else if (t > 1f) { t = 1f; }
            SetVelocity(t * MaxDeflectRad);
            UISounds.Play("FX/MinigameSlap01", 0.5f);
        }

        private void CollideBricks() {
            var bL = ballX - BallHalf;
            var bR = ballX + BallHalf;
            var bB = ballY - BallHalf;
            var bT = ballY + BallHalf;
            var c0 = ColOf(bL);
            var c1 = ColOf(bR);
            var r0 = RowOf(bT);
            var r1 = RowOf(bB);
            for (int r = r0; r <= r1; r++) {
                if (r < 0 || r >= Rows) { continue; }
                var brickTop = FieldTop - r * CellH;
                var brickBottom = brickTop - BrickH;
                if (bB >= brickTop || bT <= brickBottom) { continue; }
                for (int c = c0; c <= c1; c++) {
                    if (c < 0 || c >= Cols) { continue; }
                    var idx = r * Cols + c;
                    if (hp[idx] == 0) { continue; }
                    var x0 = PlayLeft + c * CellW + GapInset;
                    var x1 = x0 + BrickW;
                    if (bR <= x0 || bL >= x1) { continue; }

                    // resolve on the axis of least penetration
                    var overlapX = Mathf.Min(bR - x0, x1 - bL);
                    var overlapY = Mathf.Min(brickTop - bB, bT - brickBottom);
                    if (overlapX < overlapY) {
                        if (ballX < x0 + BrickW / 2f) {
                            ballX -= overlapX;
                            velX = -Mathf.Abs(velX);
                        } else {
                            ballX += overlapX;
                            velX = Mathf.Abs(velX);
                        }
                    } else {
                        if (ballY < brickBottom + BrickH / 2f) {
                            ballY -= overlapY;
                            velY = -Mathf.Abs(velY);
                        } else {
                            ballY += overlapY;
                            velY = Mathf.Abs(velY);
                        }
                    }
                    HitBrick(idx);
                    return;
                }
            }
        }

        private void HitBrick(int idx) {
            hp[idx]--;
            Score++;
            if (hp[idx] != 0) {
                UISounds.Play("FX/MinigameHit01", 0.5f);
                dirty = true;
                return;
            }
            UISounds.Play("FX/MinigameExplode01", 0.6f);
            Score += 2;
            bricksLeft--;
            if (bricksLeft == 0) {
                NextLevel();
                return;
            }
            dirty = true;
        }

        private void NextLevel() {
            level++;
            speed = SpeedFor();
            BuildBricks();
            ResetBall();
            dirty = true;
            UISounds.Play("FX/MinigameWin01");
        }

        private void LoseLife() {
            lives--;
            dirty = true;
            UISounds.Play("FX/MinigameLose01", 0.75f);
            if (lives <= 0) {
                Die();
                return;
            }
            ResetBall();
        }

        private void Die() {
            IsDead = true;
            dirty = true;
        }

        private float SpeedFor() {
            var factor = Mathf.Pow(LevelSpeedMul, level - 1);
            if (factor > MaxSpeedFactor) {
                factor = MaxSpeedFactor;
            }
            return SpeedBase * factor;
        }

        private void BuildBricks() {
            bricksLeft = Cols * Rows;
            for (int r = 0; r < Rows; r++) {
                byte startHp = r < 2 ? (byte) 3 : r < 4 ? (byte) 2 : (byte) 1;
                for (int c = 0; c < Cols; c++) {
                    hp[r * Cols + c] = startHp;
                }
            }
        }

        // Launches upward from the paddle with a slight random tilt. Rng.URange
        // routes through Rng.Unseeded, keeping the simulation's seeded stream
        // untouched (same precedent as Snake food placement).
        private void ResetBall() {
            ballX = paddleX;
            ballY = PaddleY + PaddleH + BallHalf + 1f;
            SetVelocity(Rng.URange(-0.35f, 0.35f) * MaxDeflectRad);
        }

        private void SetVelocity(float angle) {
            velX = Mathf.Sin(angle) * speed;
            velY = Mathf.Cos(angle) * speed;
        }

        // Grid band mapping for collision candidates. Bands are coarse (they
        // include the gaps between bricks); the exact rect overlap tests in
        // CollideBricks filter them.
        private static int ColOf(float x) {
            return (int) ((x - PlayLeft) / CellW);
        }

        private static int RowOf(float y) {
            return (int) ((FieldTop - y) / CellH);
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

        private static void DrawRect(float x0, float y0, float x1, float y1) {
            GL.Vertex3(x0, y0, 0f);
            GL.Vertex3(x0, y1, 0f);
            GL.Vertex3(x1, y1, 0f);
            GL.Vertex3(x1, y0, 0f);
        }

        private sealed class BreakoutState : MinigameState {
            public byte[] Hp;
            public float PaddleX;
            public float BallX;
            public float BallY;
            public float VelX;
            public float VelY;
            public float Speed;
            public int Lives;
            public int Level;
            public int BricksLeft;
            public int Score;
        }
    }
}
