using Game.Constants;
using Game.Utils;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionCancelFlight : SOAction {
        public const string ActionId = "cancel_flight";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionCancelFlight());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (!ctx.S.Sys.Ship.HasDestination(out var pos)) {
                return false;
            }
            var so = ctx.SO;
            if (so == ctx.S.Sys.Ship.DestinationSO) {
                return true;
            }
            if (so.IsShip) {
                return so.Ship.IsPlayer;
            }
            return ctx.S.UIQuery.IsStarmapPosNearMouse.Ask(pos);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.ActionCancelFlight, CancelFlight);
            return true;
        }

        private void CancelFlight() {
            ctx.S.Sys.Ship.CancelFlight();
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
