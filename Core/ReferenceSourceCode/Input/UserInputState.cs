using System;
using UnityEngine;
using Game.Data;
using Game.Systems.UIUX;

namespace Game.Input {
    public sealed class UserInputState {
        public GameState S;
        public Bindings Bindings;
        public TagHandle FullScreenTag;
        public TagHandle ActAsNotUITag;

        // Persistent state across frames
        public bool IsDragging;
        public Vector3 DragStartPosition;
        public float DragDistance;
        public Vector3 LastMouseWorldPos;
        public Vector2 CancelStartPos;
        public bool ReadyForClickUp;
        public Vector2 RotatePressStartMousePos;
        /// <summary>
        /// This is for clicking links to beings in Fanfare, without it, click down
        /// clicks through fanfare text on the ground and deselects the being on click up
        /// </summary>
        public bool ConsumeNextClickUp;

        // Per-frame state (set at the top of each Update tick)
        public bool EscWasPressed;
        public bool EscClosesUI;
        public bool IsMouseOverUI;
        public bool ClickWasPressed;
        public bool ClickWasReleased;

        public void ConsumeClick() {
            // D.Warn("consuming click");
            IsDragging = false;
            ClickWasPressed = false;
            ClickWasReleased = false;
        }

        public Vector3 MousePosition() {
            var mouseSlack = Bindings.Mouse.Slack;
            var mousePos = Bindings.Mouse.WorldPosition;
            if (Math.Abs(LastMouseWorldPos.x - mousePos.x) < mouseSlack
                    && Math.Abs(LastMouseWorldPos.y - mousePos.y) < mouseSlack) {
                mousePos = LastMouseWorldPos;
            } else {
                LastMouseWorldPos = mousePos;
            }
            return mousePos;
        }
    }
}
