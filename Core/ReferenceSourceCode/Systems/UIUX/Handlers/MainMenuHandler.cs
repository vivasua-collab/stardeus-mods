using System;
using Game.Constants;
using Game.Data;
using Game.UI;

namespace Game.Systems.UIUX {
    public sealed class MainMenuHandler : BaseWidgetHandler<MainMenuWidget> {
        public MainMenuHandler(GameState s, MainMenuWidget widget) : base(s, widget) {
            widget.Initialize();
            s.UIQuery.ShowMainMenu.SetResponder(Show);
            s.UIQuery.HideMainMenuInstant.SetResponder(HideInstant);
            s.UIQuery.CloseMainMenu.SetResponder(Close);
            s.UIQuery.ShowMainMenuSettingsPage.SetResponder(ShowSettingsPage);
            s.UIQuery.ShowChangelog.SetResponder(ShowChangelog);
        }

        private bool ShowSettingsPage(string id) {
            if (!UIShowing.MainMenu) {
                Show(true);
            }
            widget.ShowSettingsPage(id);
            return true;
        }

        private bool ShowChangelog() {
            if (!UIShowing.MainMenu) {
                Show(true);
            }
            widget.ShowChangelog();
            return true;
        }

        private bool HideInstant() {
            widget.DoHide();
            return true;
        }

        private bool Close(bool instant) {
            if (G.IsDemoMode) { return false; }
            if (!UIShowing.MainMenu) { return false; }
            widget.FullyClose(instant);
            return true;
        }

        private bool Show(bool show) {
            if (UIShowing.MainMenu == show) { return false; }
            if (show) {
                widget.Show();
            } else {
                widget.DrillUp();
                // widget.Hide();
            }
            return true;
        }
    }
}
