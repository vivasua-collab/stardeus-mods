using Game.Constants;
using Game.Data;
using Game.Systems;
using Game.Systems.UIUX;
using Game.Tools;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Input {
    public sealed class UserInputToolHandler {
        private readonly UserInputState state;

        public UserInputToolHandler(UserInputState state) {
            this.state = state;
        }

        public void HandleActiveTool() {
            if (!Ready.Input) { return; }
            if (state.IsMouseOverUI) {
                if (!state.IsDragging) {
                    #if UNITY_EDITOR
                    // D.Log("Mouse is overUI: {1} {0}",
                    //     D.ObjPath(MouseUtils.FirstUnderCursor), state.Bindings.Mouse);
                    #endif
                    // Without a hover the tool would keep drawing what it showed
                    // for the tile the cursor left
                    state.S.Toolbox.ActiveTool?.OnHoverUI();
                    return;
                }
            }
            var tool = state.S.Toolbox.ActiveTool;
            if (tool != null) {
                HandleTool(tool);
            }
        }

        public void HandlePassiveTools() {
            var pt = state.S.Toolbox.PassiveTools;
            var ptc = pt.Count;
            for (int i = 0; i < ptc; i++) {
                var tool = pt[i];
                if (!tool.IsAvailable) { continue; }
                HandleTool(tool);
            }
        }

        private void HandleTool(ITool tool) {
            var mousePos = state.MousePosition();
            tool.OnHover(mousePos);
            if (state.IsDragging) {
                state.DragDistance = Vectors.Distance(state.DragStartPosition, mousePos);
                if (state.DragDistance > Consts.DragMinDist) {
                    tool.OnDrag(state.DragStartPosition, mousePos, state.DragDistance);
                    state.S.Vars.LastInputTime = GameTimer.realtimeSinceStartupAsDouble;
                }
            }
            if (state.Bindings.IsJustPressed(ActionType.Rotate)) {
                state.RotatePressStartMousePos = state.Bindings.Mouse.ScreenPosition;
            }

            // Allow some slack when dragging
            var pixelSlackSqr = Mathf.Pow(Screen.width / 50, 2);

            if (state.Bindings.IsJustReleased(ActionType.Rotate)) {
                if (!state.Bindings.IsJustReleased(ActionType.CameraDrag) ||
                    Vectors.SqrDistance(
                        state.RotatePressStartMousePos,
                        state.Bindings.Mouse.ScreenPosition) < pixelSlackSqr) {
                    tool.OnRotate(1);
                }
            }
            if (state.ClickWasPressed) {
                // We will cycle RMB + LMB, no need to do anything here
                if (state.Bindings.IsPressed(ActionType.ClickCancel)) {
                    state.ReadyForClickUp = true;
                    return;
                }
                if (tool is IsDoubleClickableTool dcl &&
                        state.Bindings.IsDoubleClickedUnchecked(ActionType.Click)) {
                    if (dcl.OnDoubleClick(mousePos)) {
                        return;
                    }
                }
                tool.OnClickDown(mousePos);
                state.ReadyForClickUp = true;
                state.IsDragging = true;
                state.DragStartPosition = mousePos;
                state.S.Vars.LastInputTime = GameTimer.realtimeSinceStartupAsDouble;
            }
            if (state.ClickWasReleased) {
                // This cancels drag if mouse gets over ui. I can't remember why
                // this was done, but there must have been a good reason.
                // if (state.IsMouseOverUI && state.IsDragging) {
                //     tool.CancelDrag();
                //     state.ReadyForClickUp = false;
                // }
                if (state.ReadyForClickUp) {
                    state.ReadyForClickUp = false;
                    // RMB + LMB will cycle
                    if (state.Bindings.IsPressed(ActionType.ClickCancel)) {
                        state.S.Sig.CycleTool.Send(1);
                    } else {
                        tool.OnClickUp(state.DragStartPosition, mousePos, state.DragDistance);
                    }
                }
                state.IsDragging = false;
            }
        }
    }
}
