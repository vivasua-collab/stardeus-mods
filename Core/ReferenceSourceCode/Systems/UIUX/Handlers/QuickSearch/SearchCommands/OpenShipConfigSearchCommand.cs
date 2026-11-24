using System;
using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Console;

namespace Game.Systems.UIUX {
    public class OpenShipConfigSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenShipConfigSearchCommand());
        }
        public const string CmdId = "OpenShipConfig";
        public override string Id => CmdId;
        public override string Icon => IconId.CSpaceShip;
        protected override string NameT => T.ShipConfiguration;
        public override string DescT => T.ShipConfiguration;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        public override bool IsEnabled => true;

        public override bool Execute() {
            A.S.UIQuery.ToggleShipConfig.Ask(!UIShowing.ShipConfig);
            return true;
        }
    }
}
