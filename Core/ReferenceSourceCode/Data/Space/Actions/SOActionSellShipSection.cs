using Game.CodeGen;
using Game.Constants;
using Game.UI;
using Game.Systems.UIUX;

using Game.Utils;
namespace Game.Data.Space {
    public sealed class SOActionSellShipSection : SOAction {
        public const string ActionId = "SellShipSection";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionSellShipSection());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Type.IdH == SpaceObjectTypeIdH.Port) {
                return ctx.S.Sys.Ship.IsNearby(ctx.SO);
            }
            return false;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.SellShipSection, InitiateSellSection, T.SellShipSectionHint);
            return true;
        }

        private void InitiateSellSection() {
            UIUtils.CloseFullScreenUI();
            ctx.S.Sig.ShowOverlay.Send(OverlayId.Islands);
        }

        public override string Id => ActionId;
    }
}
