using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Tools;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Input {
    public sealed class UserInputShortcutsHandler {
        private readonly UserInputState state;

        public UserInputShortcutsHandler(UserInputState state) {
            this.state = state;
        }

        public void HandleShortcuts() {
            if (state.EscWasPressed || !Ready.Input) {
                return;
            }

            var S = state.S;
            var bindings = state.Bindings;

            if (bindings.IsJustPressed(ActionType.OpenResearch)) {
                if (UIShowing.Research) {
                    S.UIQuery.ShowResearch.Ask(false);
                } else {
                    if (UIShowing.BuildMenu) {
                        S.UIQuery.ShowBuildMenu.Ask(false);
                    }
                    S.UIQuery.ShowResearch.Ask(true);
                }
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.OpenJobs)) {
                S.UIQuery.ToggleJobMatrix.Ask(!UIShowing.JobMatrix);
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.OpenStarmap)) {
                if (UIShowing.Starmap) {
                    S.UIQuery.ShowStarmap.Ask(false);
                } else {
                    if (UIShowing.BuildMenu) {
                        S.UIQuery.ShowBuildMenu.Ask(false);
                    }
                    S.UIQuery.ShowStarmap.Ask(true);
                }
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.OpenInventory)) {
                S.UIQuery.ToggleInventoryPanel.Ask();
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.OpenProduction)) {
                S.UIQuery.ToggleProduction.Ask(!UIShowing.Production);
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.OpenSurgeries)) {
                S.UIQuery.ToggleSurgeries.Ask(!UIShowing.Surgeries);
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.OpenQuests)) {
                S.UIQuery.ShowQuests.Ask(!UIShowing.Quests);
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.ShortcutApps)) {
                ShipComputerComp.ShowApps(S);
                UISounds.PlayButtonClick();
            } else if (bindings.IsJustPressed(ActionType.CenterView)) {
                if (UIShowing.FullScreenWidget) {
                    S.Sig.RefocusWidget.Send("CenterView shortcut");
                } else {
                    EntityUtils.CameraFocusOnShip();
                }
            } else if (bindings.IsJustPressed(ActionType.CycleToolMode)) {
                S.Toolbox.ActiveTool.CycleMode();
            } else if (bindings.IsJustPressed(ActionType.ToolNext)) {
                S.Sig.CycleTool.Send(1);
            } else if (bindings.IsJustPressed(ActionType.ToolPrev)) {
                S.Sig.CycleTool.Send(-1);
            } else if (bindings.IsJustPressed(ActionType.CycleTimeSpeed)) {
                new CmdSetGameSpeed(S.Clock.NextSpeed).Execute(S);
            } else if (bindings.IsJustPressed(ActionType.ToggleTime)) {
                new CmdSetGameSpeed(0, G.ClockSpeed).Execute(S);
            } else if (bindings.IsJustPressed(ActionType.TimeSpeedNormal)) {
                new CmdSetGameSpeed(ClockSpeed.Normal).Execute(S);
            } else if (bindings.IsJustPressed(ActionType.TimeSpeedUnlimited)) {
                #if !PRODUCTION_BUILD
                new CmdSetGameSpeed(ClockSpeed.Unlimited).Execute(S);
                #else
                if (A.InstantBuild) {
                    new CmdSetGameSpeed(ClockSpeed.Unlimited).Execute(S);
                }
                #endif
            } else if (bindings.IsJustPressed(ActionType.TimeSpeedFast1)) {
                new CmdSetGameSpeed(ClockSpeed.Fast1).Execute(S);
            } else if (bindings.IsJustPressed(ActionType.TimeSpeedFast2)) {
                new CmdSetGameSpeed(ClockSpeed.Fast2).Execute(S);
            } else if (!UIShowing.FullScreenWidget) {
                var shortcuts = Toolbox.Shortcuts;
                var shortcutCount = shortcuts.Count;
                for (int i = 0; i < shortcutCount; i++) {
                    var s = shortcuts[i];
                    if (bindings.IsJustPressed(s)) {
                        S.Sig.ShortcutPressed.Send(s);
                    }
                }
            }

            // Dispatching from here gives handlers the same guards as the core
            // shortcuts: text input focus, screen block, game setup, window focus
            var handled = bindings.Handled;
            var handledCount = handled.Count;
            for (int i = 0; i < handledCount; i++) {
                var h = handled[i];
                if (bindings.IsJustPressed(h)) {
                    h.Handler.Invoke();
                    return;
                }
            }
        }
    }
}
