using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_JobMatrix : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_JobMatrix());
        }
        public const string AppId = "JobMatrix";
        public override string Id => AppId;
        public override string Name => T.NounJobs;
        public override string Icon => IconId.CWrench2;
        public override string ColorId => "Accent2";
        public override ActionType Shortcut => ActionType.OpenJobs;
        public override void OnClick() {
            A.S.UIQuery.ToggleJobMatrix.Ask(!UIShowing.JobMatrix);
        }
    }
}
