using UnityEngine;

namespace Game.Systems.Minigames {
    public abstract class Minigame {
        public abstract string Id { get; }
        public bool IsDead { get; protected set; }
        public int Score { get; protected set; }
        // Set by MinigameSys on focus changes; games must render a visible
        // paused indication while true.
        public bool IsPaused { get; private set; }

        public void SetPaused(bool on) {
            IsPaused = on;
        }

        public abstract void StartNew(RenderTexture target);
        public abstract void Resume(MinigameState state, RenderTexture target);
        // Allocated only on app switch / close-while-alive.
        public abstract MinigameState CaptureState();
        // Already edge-detected and debounced by MinigameSys.
        public abstract void OnInput(Vector2Int dir);
        // Raw combined movement vector, polled every focused frame. Games
        // needing held-state input (e.g. a paddle) use it; edge-based games
        // can ignore it and rely on OnInput alone.
        public virtual void OnRawMove(Vector2 raw) { }
        // Returns true when the board changed and needs a re-render.
        public abstract bool Step(float unscaledDt);
        public abstract void Render();
        public abstract void OnThemeColorsChanged();
    }

    public abstract class MinigameState { }
}
