using Game.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Input {
    public class RealMouse : IMouse {
        public RealMouse() {
            Cursor.visible = true;
        }
        public float Slack => MouseUtils.Slack;
        public Vector2 ScreenPosition => MouseUtils.ScreenPos;
        public Vector2 ScreenPositionUnclamped => MouseUtils.ScreenPosUnclamped;
        public Vector2 WorldPosition => MouseUtils.WorldPositionUncached;
        public GameObject FirstUnderCursor => MouseUtils.FirstUnderCursor;
        public bool IsOverUI => MouseUtils.IsOverUI;
        public bool IsOverGameView => MouseUtils.IsOverGameView;
        public bool IsOverScrollableUI => MouseUtils.IsOverScrollableUI;
    }
}
