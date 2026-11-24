using Game.Constants;
using Game.UI;
using Game.Utils;
using KL.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Diplomacy : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Diplomacy());
        }

        public override string Id => "Diplomacy";
        public override string Name => T.NounDiplomacy;
        public override string Icon => IconId.CScale;
        public override string ColorId => "Accent3";
        public override void OnClick() {
            A.S.UIQuery.ToggleDiplomacy.Ask(!UIShowing.Diplomacy);
        }
    }
}
