using Game.Constants;
using Game.CodeGen;
using Game.Systems.UIUX;
using Game.UI;
using KL.Utils;
using Game.Utils;

namespace Game.Data.Space {
    public sealed class SOActionShipInfo : SOAction {
        public override int UIPriority => int.MaxValue;
        public const string ActionId = "ShipInfo";

        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionShipInfo());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return ctx.SO.Ship != null && !ctx.SO.Ship.IsDerelict;
        }

        public override bool AddOptions(QuickChoice qc) {
            var ship = ctx.SO.Ship;
            qc.AddIconButton(ship.NameFull, new QuickChoice.IconButton() {
                AutoClose = false,
                Icon = Images.Sprite(ship.Class.Preview),
                OnClick = () => ctx.S.UIQuery.StarmapFocusOn.Ask(ship.SO, true),
                TipFunc = ShowShipInfo,
            });
            return true;
        }

        private string ShowShipInfo() {
            var ship = ctx.SO.Ship;
            return ship.FullTooltip(true);
        }

        private void ShowShipPopup() {
            var ship = ctx.SO.Ship;
            WPopup.Create(false, ship.Class.Preview, ship.NameFull, null,
                ship.FullTooltip(true)).Spawn();
        }

        public override string Id => ActionId;
    }
}
