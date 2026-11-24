using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    // The ship combat view, reachable outside combat too, so the player can check flux,
    // shields and stations before a fight instead of only during one. Locked until Battle
    // Stations can be built - before that the view has nothing to man and is a dead end
    public class ShipComputerApp_BattleStations : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_BattleStations());
        }

        public override string Id => "BattleStations";
        public override string Name => T.NounBattleStationPlural;
        public override string Icon => IconId.CAttackTarget;
        public override string ColorId => "Red";
        public override Def RequiredResearch => The.Defs.FastGet(DefIdsH.ObjectsDevicesBattleStation);
        public override void OnClick() {
            A.S.UIQuery.ToggleShipCombat.Ask(!UIShowing.ShipCombatMode);
        }
    }
}
