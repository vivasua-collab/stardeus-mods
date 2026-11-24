using Game.Constants;
using Game.Platform;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Twitch : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Twitch());
        }

        public override string Id => "Twitch";
        public override string Name => "Twitch";
        public override string Icon => GraphicsId.IconsColorTwitchGlitch;
        public override string ColorId => "Blue";
        public override bool IsAvailable => TwitchSys.IsActive;
        public override bool IsVisible => TwitchSys.IsActive;
        public override void OnClick() {
            ToggleTwitch();
        }

        public static void ToggleTwitch() {
            if (TwitchSys.IsActive) {
                if (A.S.Sys.Twitch.IsDeactivated) {
                    WPopup.Warning(T.TwitchIntegration, T.RestartRequired, SettingsPageTwitch.InactiveWarning);
                    PlatformUtils.SuggestRestart();
                    return;
                }
                A.S.UIQuery.ToggleTwitch.Ask(!UIShowing.Twitch);
            } else {
                A.S.UIQuery.ShowMainMenuSettingsPage.Ask(SettingsPageTwitch.PageId);
            }
        }
    }
}
