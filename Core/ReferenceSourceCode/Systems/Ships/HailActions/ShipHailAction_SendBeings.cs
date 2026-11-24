using Game.Components;
using Game.Constants;
using Game.Systems.Creatures;
using Game.Systems.Diplomacy;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    /// <summary>
    /// Player-to-NPC hail action that hands guests (ColonyRole.Passenger) of the target's
    /// own faction back to a ship or station of that faction for attitude and payment.
    /// All sendable guests ride in a single follow-up gift exchange package
    /// (see NPCShipBehavior_Sector_ExchangeGift, GiftMode.SendBeings). There is no
    /// NPC-initiated variant.
    /// </summary>
    public sealed class ShipHailAction_SendBeings : ShipHailAction, IShipHailAction_P2N {
        public const string ActId = "SendBeings";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_SendBeings());
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsInCombat) { return false; }
            var fac = target.Faction;
            if (fac == null || fac.IsBlank || fac.IsInternal || fac.IsPlayer) {
                return false;
            }
            // Counting only - passing no list avoids touching a shared scratch collection
            // while the hail panel is being built.
            return PassengerUtils.CollectSendableGuests(target.SO.S, fac, null) > 0;
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target,
                Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button,
                container, idx++);
            btn.Load(IconId.CPersona, T.VerbSendBeings, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send(
                        $"{T.OperationFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnHailNPC(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => T.VerbSendBeingsTip);
        }

        internal void OnHailNPC(NPCShip target, ShipHailActionUI ui) {
            if (IsAttackingUs(target)) {
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.Generic,
                    ShipChatResponseType.Negative);
                return;
            }
            // Their own people are being returned for payment - the ship always agrees
            // to come pick them up (the package offer itself is also always accepted).
            // That holds for outlaws too: refusing to trade with a ship over its class
            // limits is about trade, not about taking their people home (STA-1636).
            ui.SetResponse(isLastMsg: false, ShipChatTopic.Generic,
                ShipChatResponseType.Positive);
            if (!CanFlyToPlayerOrIsNearby(target, ui)) { return; }
            NPCShipBehavior_Sector_ExchangeGift.AssignTo(target, A.Player,
                GiftDirection.PlayerToNPC, GiftMode.SendBeings);
        }
    }
}
