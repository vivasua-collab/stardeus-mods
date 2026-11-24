using UnityEngine;

namespace Game.Input {
    public interface IMouse {
        float Slack { get; }
        Vector2 ScreenPosition { get; }
        Vector2 ScreenPositionUnclamped { get; }
        Vector2 WorldPosition { get; }
        bool IsOverUI { get; }
        bool IsOverScrollableUI { get; }
        GameObject FirstUnderCursor { get; }
        bool IsOverGameView { get; }
    }
}
