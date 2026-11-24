// #define DEBUG_UI_CLOSE
using Game.CodeGen;
using Game.Constants;
using Game.Systems;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Input {
    public sealed class UserInputEscHandler {
        private readonly UserInputState state;

        public UserInputEscHandler(UserInputState state) {
            this.state = state;
        }

        // Returns true if Update() should return immediately
        public bool HandleEscToggleMainMenu() {
            if (!UserInput.EscTogglesMainMenu) { return false; }
            // Tutorial widget blocks the Main Menu from being clickable, so don't let
            // ESC open it while Tutorial is showing
            if (UIShowing.Tutorial && !UIShowing.ImageBlowout && !UIShowing.QuickChoice) {
                UISounds.PlayActionDenied();
                return true;
            }
            // close the open menu first
            if (UIShowing.MainMenu) {
                state.S.Sig.PausePressed.Send();
                return true;
            }
            if ((UIShowing.AnyInGame || !The.Toolbox.IsFreeSelect
                    || The.Dialog.TryCloseWithEsc()) && state.EscClosesUI) {
                // Let the logic in this method handle closing stuff
                return false;
            }
            // we may be trying to close the camera link
            if (state.S.Query.CameraHideLink.Ask()) {
                return true;
            }
            if (state.EscClosesUI && state.S.UIQuery.TryCloseTopmostPanel.Ask()) {
                return true;
            }
            state.S.Sig.PausePressed.Send();
            // Nothing to do, everything should be handled in pause pressed
            return true;
        }

        private bool CloseAnyUI(bool isShowingClosableOverlay) {
            if (UIShowing.ImageBlowout) {
                #if DEBUG_UI_CLOSE
                D.Warn("Close UI: Image blowout");
                #endif
                The.SysSig.HideImageBlowout.Send();
                return true;
            }

            // why are we hiding the link?
            // answer: because UI often shows ui links to stuff
            if (state.S.Query.CameraHideLink.Ask()) {
                #if DEBUG_UI_CLOSE
                D.Warn("Close UI: Camera link");
                #endif
                return true;
            }

            if (state.EscWasPressed && !state.EscClosesUI) {
                return false;
            }

            // if (state.EscWasPressed && UIShowing.MouseTooltip && !UIShowing.FullScreenWidget) {
            //     The.SysSig.HideTooltip.Send();
            //     return true;
            // }

            if (state.EscWasPressed && state.S.UIQuery.TryCloseAllCodexPopups.Ask()) {
                return true;
            }

            // D.Warn("getting to this");
            if (state.EscWasPressed) {
                if (state.S.UIQuery.TryCloseTopmostPanel.Ask()) {
                    return true;
                }

                if (The.Dialog.TryCloseWithEsc()) {
                    #if DEBUG_UI_CLOSE
                    D.Warn("Close UI: Dialog with ESC");
                    #endif
                    return true;
                }
            }

            if (UIShowing.JobMatrix) {
                #if DEBUG_UI_CLOSE
                D.Warn("Close UI: Job matrix");
                #endif
                state.S.UIQuery.ToggleJobMatrix.Ask(false);
                return true;
            }
            if (UIShowing.BuildMenu) {
                #if DEBUG_UI_CLOSE
                D.Warn("Close UI: Build menu");
                #endif
                state.S.UIQuery.ShowBuildMenu.Ask(false);
                // state.S.Sig.ShowToolMenu.Send(false);
                return true;
            }
            if (UIShowing.QuickSearch) {
                #if DEBUG_UI_CLOSE
                D.Warn("Close UI: Quick search");
                #endif
                state.S.UIQuery.ShowQuickSearch.Ask(false);
                return true;
            }
            // order is important for last items
            if (isShowingClosableOverlay) {
                #if DEBUG_UI_CLOSE
                D.Warn("Close UI: Overlay {0}", UIShowing.OverlayType);
                #endif
                state.S.Sig.HideOverlay.Send();
                // don't return yet!
            }

            if (UIShowing.Starmap) {
                state.S.UIQuery.ShowStarmap.Ask(false);
                return true;
            }
            if (UIShowing.Research) {
                state.S.UIQuery.ShowResearch.Ask(false);
                return true;
            }

            if (UIShowing.ShipCombatMode) {
                state.S.UIQuery.ToggleShipCombat.Ask(false);
            }

            if (!isShowingClosableOverlay) {
                if (state.EscWasPressed && UIShowing.BeingGroup) {
                    #if DEBUG_UI_CLOSE
                    D.Warn("Close UI: Being group on esc");
                    #endif
                    state.S.Query.ToggleBeingGroupUI.Ask(null);
                    return true;
                }
            }
            return isShowingClosableOverlay;
        }

        /// <returns>true if some UI was closed</returns>
        public bool HandleCancelReleased(bool isShowingClosableOverlay) {
            if (!state.EscWasPressed) {
                var dragDist = The.Prefs.GetInt(Pref.GCancelDragDistancePixels,
                    PrefH.GCancelDragDistancePixels,
                        Consts.DefaultCancelDragDistancePx);
                var currentDragDist = Vectors.SqrDistance(
                    state.Bindings.Mouse.ScreenPositionUnclamped, state.CancelStartPos);
                if (currentDragDist >= dragDist * dragDist) {
                    return false;
                }
            }

            // Handle tools last, if we didn't do anything with the ui
            if (state.S.Toolbox.IsFreeSelect) {
                bool tryBeingCtx = false;
                if (!state.IsMouseOverUI && !state.EscWasPressed) {
                    var selectedBeings = state.S.Query.GetSelectedBeings.Ask();
                    if (!Arrays.IsEmpty(selectedBeings)) {
                        foreach (var b in selectedBeings) {
                            if (b.AIAgent.DirectControl.IsUnderDirectControl) {
                                tryBeingCtx = true;
                                break;
                            } else if (b.Data.IsPlayer && !b.IsDead && !b.Data.Body.IsIncapacitated) {
                                tryBeingCtx = true;
                                break;
                            }
                        }
                    }
                }
                bool needsCheckCancel = true;
                if (tryBeingCtx) {
                    needsCheckCancel = false;
                    if (CheckCancelFreeSelect()) {
                        return true;
                    }
                }
                if (CloseAnyUI(isShowingClosableOverlay)) {
                    UISounds.PlayCancel();
                    return true;
                }
                if (!state.IsMouseOverUI || state.EscWasPressed) {
                    if (needsCheckCancel && (!state.EscWasPressed || state.EscClosesUI)) {
                        CheckCancelFreeSelect();
                    }
                }
            } else {
                if (state.S.Toolbox.ActiveTool.CancelDrag()) {
                    state.DragStartPosition = state.MousePosition();
                } else {
                    // If we were in some other tool, this will take care of it
                    state.S.Toolbox.Clear();
                }
                UISounds.PlayCancel();
            }
            return false;
        }

        private bool CheckCancelFreeSelect() {
            var fs = state.S.Toolbox.FreeSelect;
            bool cancelled = false;
            if (!state.EscWasPressed) {
                var invokedCtxMenuOnSelectedObj = fs.ClickCancelReleased();
                if (invokedCtxMenuOnSelectedObj) {
                    cancelled = true;
                    UISounds.PlayButtonClick();
                } else if (!state.S.Query.IsAnythingSelected.Ask()) {
                    // we won't show tool menu anymore on right click
                    // A.S.UIQuery.ShowBuildMenu.Ask(true);
                    return true;
                } else {
                    // something is selected, let's not clear the tool
                    cancelled = true;
                }
            }
            if (!cancelled) {
                cancelled = fs.Cancel();
            }
            if (!cancelled) {
                state.S.Toolbox.Clear();
            }
            state.DragStartPosition = state.MousePosition();
            state.DragDistance = 0;
            return cancelled;
        }
    }
}
