using Game.Constants;
using Game.Systems.Twitch;
using Game.UI;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenTwitchQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenTwitchQuickSearchCommand());
        }

        public const string CmdId = "OpenTwitch";
        public override string Id => CmdId;
        public override string Icon => GraphicsId.IconsColorTwitchGlitch;
        protected override string NameT => "Twitch";
        public override string DescT => T.TwitchIntegration;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => TwitchSys.IsActive;

        public override bool Execute() {
            ShipComputerApp_Twitch.ToggleTwitch();
            return true;
        }
    }
}
