using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_CopterMissions : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_CopterMissions());
        }

        public override string Id => "CopterMissions";
        public override string Name => T.CopterMissions;
        public override string Icon => IconId.CCopter;
        public override string ColorId => "Yellow";

        public override Def RequiredResearch => The.Defs.FastGet(DefIdsH.ResearchSpaceTravelMiningAutomation);
        public override void OnClick() {
            A.S.Sig.ShowOverlay.Send(OverlayId.Copters);
        }
    }
}
