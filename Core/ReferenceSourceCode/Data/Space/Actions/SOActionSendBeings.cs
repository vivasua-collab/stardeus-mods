using System.Text;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Systems.Creatures;
using Game.Systems.Diplomacy;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Randomness;
using KL.Utils;

namespace Game.Data.Space {
    // Drops guests off at a planet space port of their own faction while the ship is next
    // to it. Same package as the Send Beings hail (SendBeingsGift), with nobody flying over,
    // so declining costs no standing.
    public sealed class SOActionSendBeings : SOAction {
        public const string ActionId = "SendBeings";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionSendBeings());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.Type.IdH != SpaceObjectTypeIdH.Port) { return false; }
            var fac = ctx.SO.Faction;
            if (fac == null || fac.IsBlank || fac.IsInternal || fac.IsPlayer) { return false; }
            if (!ctx.S.Sys.Ship.IsNearby(ctx.SO)) { return false; }
            return PassengerUtils.CollectSendableGuests(ctx.S, fac, null) > 0;
        }

        public override bool AddOptions(QuickChoice qc) {
            var btn = new QuickChoice.Button() {
                AutoClose = true,
                OnClick = Start,
                TipFunc = () => T.VerbSendBeingsTip,
            };
            qc.AddButton(T.VerbSendBeings, btn);
            return true;
        }

        public void Start() {
            var s = ctx.S;
            StringBuilder sb = null;
            if (!A.HasCommunicator) {
                sb = new StringBuilder();
                sb.AppendFormat("{0}:", T.OperationFailed);
                sb.AppendFormat("\n - {0}", Texts.Red(T.CommunicatorMissing));
            }
            if (TradeUI.FindTradePortal(s) == null) {
                if (sb == null) {
                    sb = new StringBuilder();
                    sb.AppendFormat("{0}:", T.OperationFailed);
                }
                sb.AppendFormat("\n - {0}", TradePortalComp.NotAvailable);
            }
            if (sb != null) {
                s.Sig.ShowErrorMessage.Send(sb.ToString());
                return;
            }

            var port = ctx.SO;
            var faction = port.Faction;
            var pack = SendBeingsGift.CreatePackage(s, faction, null, port.Trader, out _);
            if (pack == null) {
                UISounds.PlayActionDenied();
                return;
            }
            var ui = GiftChoiceUI.ForFaction(s, faction, GiftMode.SendBeings,
                GiftDirection.PlayerToNPC);
            ui.AddOption(pack);
            var cancel = GiftPackage.ForFaction(GiftDirection.PlayerToNPC, GiftMode.SendBeings,
                faction, -1);
            cancel.IsCancel = true;
            ui.AddOption(cancel);

            s.Clock.ForceStop(ForceStopReason.Choice);
            var shown = s.UIQuery.ShowGiftChoice.Ask(ui.Choice, (selected) => {
                selected.Offer(s, new Rng(Hashes.Quick(port.Seed, (uint) G.Ticks)));
                s.Clock.ForceResume(ForceStopReason.Choice);
            });
            if (!shown) {
                s.Clock.ForceResume(ForceStopReason.Choice);
                D.Err("Failed to show gift choice widget, another modal is active");
            }
        }

        public override string Id => ActionId;
    }
}
