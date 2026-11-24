using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Utils;

namespace Game.Systems.UIUX {
    public class ShipComputerApp_Paint : ShipComputerApp {
        [SelfInit]
        private static void Register() {
            Register(new ShipComputerApp_Paint());
        }

        public override string Id => "Paint";
        public override string Name => T.OverlayTipTint;
        public override string Icon => IconId.CPaint;
        public override string ColorId => "Red";
        public override Def RequiredResearch => The.Defs.FastGet(DefIdsH.ResearchHabitationPaint);

        public override void OnClick() {
            A.S.Sig.ToggleOverlayById.Send(OverlayId.Tint);
        }
    }
}
