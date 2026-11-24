using Game.CodeGen;
using Game.Constants;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class OpenBattleStationsQuickSearchCommand : BaseSearchCommand {
        [SelfInit]
        private static void Register() {
            Register(CmdId, new OpenBattleStationsQuickSearchCommand());
        }

        public const string CmdId = "OpenBattleStations";
        public override string Id => CmdId;
        public override string Icon => IconId.CAttackTarget;
        protected override string NameT => T.NounBattleStationPlural;
        public override string DescT => T.NounShipCombat;
        public override string AdapterId => OpenUISearchAdapter.AdapterId;
        // Same lock as ShipComputerApp_BattleStations
        public override bool IsEnabled => A.S.Research.IsDefUnlocked(
            The.Defs.FastGet(DefIdsH.ObjectsDevicesBattleStation));

        public override bool Execute() {
            A.S.UIQuery.ToggleShipCombat.Ask(true);
            return true;
        }
    }
}
