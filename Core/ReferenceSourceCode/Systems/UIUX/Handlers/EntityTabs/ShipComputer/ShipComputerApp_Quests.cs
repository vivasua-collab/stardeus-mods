using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Quests : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Quests());
        }

        public override string Id => "Quests";
        public override string Name => T.Quests;
        public override string Icon => IconId.CQuest;
        public override string ColorId => "ButtonPanelFancy2";
        public override ActionType Shortcut => ActionType.OpenQuests;
        public override void OnClick() {
            A.S.UIQuery.ShowQuests.Ask(!UIShowing.Quests);
        }
    }
}
