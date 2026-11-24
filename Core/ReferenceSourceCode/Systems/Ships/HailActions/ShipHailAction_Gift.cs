using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipHailAction_Gift : ShipHailAction, IShipHailAction_P2N,
            IShipHailAction_N2P {
        public const string ActId = "Gift";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_Gift());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CStarcreds, T.VerbOfferGift, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnHailNPC(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => {
                var chance = CalculateChanceFor(target);
                return $"{T.ChanceAccept}: {Units.PercentageColored(chance)}";
            });
        }

        private void OnHailNPC(NPCShip target, ShipHailActionUI ui) {
            if (IsAttackingUs(target)) {
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.Generic, ShipChatResponseType.Negative);
                return;
            }

            float chance = CalculateChanceFor(target);

            if (CheckActionPossibility(target, ui, ShipChatTopic.Generic,
                    target.SeededRng(Primes.Prime057), chance)) {
                if (!CanFlyToPlayerOrIsNearby(target, ui)) { return; }
                NPCShipBehavior_Sector_ExchangeGift.AssignTo(target, A.Player,
                    GiftDirection.PlayerToNPC, GiftMode.Gift);
            }
        }

        private static float CalculateChanceFor(NPCShip target) {
            Curve curve = Tunables.NPCShipGift_AttitudeChance;
            var chance = curve.Evaluate(target.AttitudeTowardsPlayer);
            return chance;
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (target.IsInCombat) { return false; }
            if (target.Memory.HasCustom) {
                if (target.Memory.TryCustom(
                        NPCShipBehavior_Sector_ExchangeGift.Key_Long_GiftAvailableAfter,
                            out long availableAfter)) {
                    return G.Ticks > availableAfter;
                }
            }
            return true;
        }

        public static void HailPlayer(NPCShip ship) {
            var act = Registries.ShipHailActions.FastGet(ActIdH);
            ShipHailActionUI.For(ship.SO.S, ship, true, act)?.HailPlayer(act);
        }

        public void N2P_LoadPlayerChoice(ShipHailActionUI actUI, NPCShip caller, Lifetime life, RectTransform container, ref int idx) {
            actUI.IsWaitingForPlayerChoice = true;
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CStarcreds, T.VerbAccept, () => {
                NPCShipBehavior_Sector_ExchangeGift.DoStartExchange_N2P(caller, GiftMode.Gift);
            });
        }


        public void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship) {
            ui.SetResponse(
                isLastMsg: false,
                ShipChatTopic.GiftRequest, ShipChatResponseType.Neutral);
        }

        public void N2P_OnPlayerChoiceTimeout(NPCShip caller) {
            // TODO impact relationships
            caller.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
        }
    }
}
