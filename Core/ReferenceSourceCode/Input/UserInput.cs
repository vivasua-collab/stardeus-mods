using UnityEngine;
using KL.Console;
using Game.CodeGen;
using Game.Data;
using KL.Utils;
using Game.Constants;
using Game.UI;
using UnityEngine.InputSystem;

namespace Game.Input {
    [DefaultExecutionOrder(-11000)]
    public class UserInput : MonoBehaviour {
        private readonly UserInputState state = new();
        private UserInputEscHandler escHandler;
        private UserInputShortcutsHandler shortcutsHandler;
        private UserInputToolHandler toolHandler;

        public static int InputLagNS = 0;

        // 0 - toggle main menu only
        // 1 - close all ui, then toggle main menu
        // 2 - close ui only, don't toggle main menu
        public static bool EscClosesUI => The.Prefs.GetInt(
            Pref.GESCBehavior, PrefH.GESCBehavior, Consts.DefaultEscBehavior) != 0;
        public static bool EscTogglesMainMenu => The.Prefs.GetInt(
            Pref.GESCBehavior, PrefH.GESCBehavior, Consts.DefaultEscBehavior) != 2;

        private void Start() {
            escHandler = new UserInputEscHandler(state);
            shortcutsHandler = new UserInputShortcutsHandler(state);
            toolHandler = new UserInputToolHandler(state);
            The.SysSig.AfterLoadSaveState.AddListener(OnLoadedSave, gameObject);
            The.SysSig.TextLinkClicked.AddListener(OnTextLinkClicked, gameObject);
            InGameConsole.ConsoleStateChanged.AddListener(
                OnConsoleStateChange, gameObject);
            InputLagNS = 1000;
            state.Bindings = The.Bindings;
            state.Bindings.IsLocked = InGameConsole.IsVisible;
            state.FullScreenTag = TagHandle.GetExistingTag("FullScreenWidget");
            state.ActAsNotUITag = TagHandle.GetExistingTag("ActAsNotUI");
            // this kills mouse click-drags on on PC!
            // TouchSimulation.Enable();
        }

        private void OnDestroy() {
            if (state.S != null) {
                state.S.Sig?.PausePressed.RemoveListener(OnPausePressed);
                state.S = null;
            }
            The.SysSig.AfterLoadSaveState.RemoveListener(OnLoadedSave);
            The.SysSig.TextLinkClicked.RemoveListener(OnTextLinkClicked);
            InGameConsole.ConsoleStateChanged.RemoveListener(OnConsoleStateChange);
        }

        private void OnLoadedSave(GameState s) {
            state.S = s;
            state.S.Sig.PausePressed.AddListener(OnPausePressed, gameObject);
            state.S.Sig.ShortcutPressed.AddListener(state.S.Toolbox.ActivateShortcut, gameObject);
        }

        private bool CheckFocus() {
            // scripted captures drive the game with the window unfocused
            if (state.Bindings.IgnoreAppFocus) {
                return true;
            }
            #if !PRODUCTION_BUILD
            if (state.Bindings.HasSimJustPressed || state.Bindings.HasSimControls) {
                return true;
            }
            #endif
            // Allow mouse to be anywhere when using gamepad
            if (VirtualMouseInput.IsCurrent) { return true; }
            //D.Log("Over game view: {0}", bindings.Mouse.IsOverGameView);
            bool isFocused = !Application.isFocused || !state.Bindings.Mouse.IsOverGameView || !state.S.IsLoaded;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                // otherwise faking tool actions in tests would not work if mouse is not over the game view
                isFocused = false;
            }
            #endif
            if (isFocused) {
                if (state.IsDragging && state.ClickWasReleased) {
                    toolHandler.HandleActiveTool();
                }
                return false;
            }
            return true;
        }

        public void Update() {
            #if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.f11Key.wasPressedThisFrame) {
                Cursor.visible = !Cursor.visible;
            }
            #endif
            if (!GameState.IsCurrent(state.S) || !Ready.TextInputFocus || !Ready.Game || UIShowing.ScreenBlock || UIShowing.GameSetup) {
                ValidateTextInputFocus();
                if (UIShowing.QuickSearch && state.Bindings.IsJustPressed(ActionType.QuickSearch)) {
                    state.S.UIQuery.ShowQuickSearch.Ask(false);
                }
                return;
            }

            state.EscWasPressed = UIPoolRoot.Activator.CancelAction.WasPressedThisFrame()
                || state.Bindings.IsJustPressed(ActionType.Pause);
            state.EscClosesUI = false;

            if (state.EscWasPressed) {
                state.EscClosesUI = EscClosesUI;
                if (state.S.UIQuery.CancelQuickChoice.Ask()) {
                    return;
                }
                // A panel modal sits above every other window, so it gets Esc first
                if (UIShowing.PanelModal && state.S.UIQuery.TryClosePanelModal.Ask()) {
                    return;
                }
                if (The.Toolbox.ActiveTool.IsClickedDown) {
                    if (The.Toolbox.ActiveTool.CancelDrag()) {
                        return;
                    }
                }
                if (escHandler.HandleEscToggleMainMenu()) {
                    return;
                }
            }
            state.IsMouseOverUI = state.Bindings.Mouse.IsOverUI;

            state.ClickWasPressed = state.Bindings.IsJustPressed(ActionType.Click);
            if (state.ClickWasPressed) {
                if (!state.IsMouseOverUI) {
                    // if (UIShowing.BuildMenu) {
                    //     state.S.UIQuery.ShowBuildMenu.Ask(false);
                    // }
                } else {
                    var first = MouseUtils.FirstUnderCursor;
                    if (first != null) {
                        // allow click-dragging from above blackholes in starfield, etc
                        if (first.CompareTag(state.ActAsNotUITag)) {
                            state.IsMouseOverUI = false;
                        }
                    }
                }
            }
            state.ClickWasReleased = state.Bindings.IsJustReleased(ActionType.Click);

            if (state.ClickWasReleased && state.ConsumeNextClickUp) {
                state.ClickWasReleased = false;
                state.ConsumeNextClickUp = false;
            }

            var cancelPressed = state.Bindings.IsJustPressed(ActionType.ClickCancel);
            var cancelWasReleased = state.Bindings.IsJustReleased(ActionType.ClickCancel);
            // a UI element took this right click as its own (WButtonText with a right click
            // action), so it is not also a cancel; read on release, like the starmap latch
            if (cancelWasReleased && G.RightClickTakenByUI) {
                G.RightClickTakenByUI = false;
                cancelWasReleased = false;
            }

            if (cancelPressed && state.S.UIQuery.CancelQuickChoice.Ask()) {
                return;
            }

            // This isn't good, because it closes more than we would want
            // if (cancelPressed && state.S.UIQuery.TryCloseTopmostPanel.Ask()) {
            //     return;
            // }
            // For sake of consistency, we won't close subpanel with RMB either
            // if (cancelPressed && UIShowing.SubPanel) {
            //     var subPanel = A.S.UIQuery.GetEntitySubPanel.Ask();
            //     if (subPanel.IsShowing) {
            //         subPanel.Close();
            //         return;
            //     }
            // }

            // Do we still need this? Yes we do!
            // Because starmap clicks should eat other stuff
            if (UIShowing.Starmap) {
                if (cancelPressed) {
                    state.CancelStartPos = state.Bindings.Mouse.ScreenPositionUnclamped;
                    cancelPressed = false;
                    state.ClickWasPressed = true;
                }
                if (cancelWasReleased) {
                    cancelWasReleased = false;
                    state.ClickWasReleased = true;
                    ClickCancelInStarmap();
                }
            }

            // Intercept the left click while showing tool menu
            var needsToolUpdate = true;
            if (UIShowing.BuildMenu) {
                // D.FIXME("consuming click");
                // if (A.Tools.IsMenuConsumingClickAction(
                //         state.ClickWasPressed, state.ClickWasReleased)) {
                //     needsToolUpdate = false;
                //     state.ConsumeClick();
                // }
            }

            if (!CheckFocus()) { return; }

            // HandlePause();

            if (!UIShowing.MainMenu &&
                    state.Bindings.IsJustPressed(ActionType.QuickSearch)) {
                // Prevent shift + tab / alt + tab from opening quick search
                if (!state.Bindings.IsEitherPressed(ActionType.ShiftModifier, ActionType.AltModifier)) {
                    if (!state.S.UIQuery.TryFocusSearchField.Ask()) {
                        state.S.UIQuery.ShowQuickSearch.Ask(!UIShowing.QuickSearch);
                    }
                }
            }

            shortcutsHandler.HandleShortcuts();

            // Don't close the overlay with RMB, if it's plan
            var isCtrlPressed = state.Bindings.IsPressed(ActionType.CtrlModifier);
            var isShowingClosableOverlay = UIShowing.Overlay && !isCtrlPressed
                && (state.EscWasPressed || (UIShowing.OverlayType != OverlayId.Plan));
                // having any being selected won't close overlay on RMB
                // && state.S.Query.SelectBeingCount.Ask() == 0;

            if (cancelPressed) {
                state.CancelStartPos = state.Bindings.Mouse.ScreenPositionUnclamped;
            }

            if (cancelWasReleased || state.EscWasPressed) {
                if (escHandler.HandleCancelReleased(isShowingClosableOverlay)) {
                    needsToolUpdate = false;
                }
            }

            if (needsToolUpdate) {
                toolHandler.HandleActiveTool();
                toolHandler.HandlePassiveTools();
            }

            if (state.ClickWasPressed) {
                state.S.Sig.ClickPressed.Send();
            } else if (state.ClickWasReleased) {
                state.S.Sig.ClickReleased.Send();
            }
        }

        private void ClickCancelInStarmap() {
            // Consume the latch here rather than on press - press/pointer-down ordering
            // between UserInput and the EventSystem isn't guaranteed within a frame.
            var clickedDownInStarmap = G.HasClickedDownInStarmap;
            G.HasClickedDownInStarmap = false;
            if (state.IsDragging) { return; }
            if (!clickedDownInStarmap) { return; }
            var dist = Vectors.Distance(state.CancelStartPos, The.Bindings.Mouse.ScreenPosition);
            var dragDist = The.Prefs.GetInt(Pref.GCancelDragDistancePixels,
                PrefH.GCancelDragDistancePixels,
                    Consts.DefaultCancelDragDistancePx);
            if (dist < dragDist) {
                state.S.UIQuery.ClickUpInStarmap.Ask(ActionType.ClickCancel);
            }
        }

        private static void ValidateTextInputFocus() {
            if (!Ready.TextInputFocus && !UIShowing.QuickChoice) {
                // validate the field
                if (Ready.TextInputFocusField == null) {
                    D.ErrSoft("TextInputFocus not ready but field is null!");
                    Ready.SetTextInputFocus(true, null, "AutoFix1");
                } else if (!Ready.TextInputFocusField.gameObject.activeInHierarchy) {
                    D.Err("TextInputFocus not ready but field game object is inactive! {0}",
                        D.GOPath(Ready.TextInputFocusField.gameObject.transform));
                    Ready.SetTextInputFocus(true, null, "AutoFix2");
                }
            }
        }

        private void OnConsoleStateChange(bool isVisible) {
            state.S?.Clock?.Pause();
            state.Bindings.IsLocked = isVisible;
            if (isVisible && UIShowing.QuickSearch) {
                state.S.UIQuery.ShowQuickSearch.Ask(false);
            }
        }

        private void OnPausePressed() {
            if (UIShowing.LoadingScreen) {
                return;
            }
            if (UIShowing.MainMenu) {
                state.S.UIQuery.ShowMainMenu.Ask(false);
                return;
            }
            if (UIShowing.ScreenBlock || !Ready.UserInput) {
                return;
            }
            if (EscClosesUI) {
                if (UIShowing.BuildMenu) {
                    state.S.UIQuery.ShowBuildMenu.Ask(false);
                    return;
                }
                if (UIShowing.Starmap) {
                    state.S.UIQuery.ShowStarmap.Ask(false);
                    return;
                }
                if (UIShowing.Research) {
                    state.S.UIQuery.ShowResearch.Ask(false);
                    return;
                }
            }
            if (!UIShowing.MainMenu) {
                state.S.UIQuery.ShowMainMenu.Ask(true);
            }
        }

        private void OnTextLinkClicked() {
            state.ConsumeClick();
            state.ConsumeNextClickUp = true;
        }

        public static string GetControlName(ActionType type) {
            return The.Bindings.GetControlName(type, anyInputMethod: false);
        }
    }
}
