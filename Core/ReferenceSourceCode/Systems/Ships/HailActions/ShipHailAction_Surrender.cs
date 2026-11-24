using System;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipHailAction_Surrender : ShipHailAction, IShipHailAction_P2N,
            IShipHailAction_N2P {
        public const string ActId = "Surrender";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_Surrender());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CStarcreds, T.VerbSurrender, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send($"{T.OperationFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnHailNPC(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => {
                var surrenderChance = NPCShipBehavior_Sector_Combat.CalculateSurrenderChance(
                    target, A.Player.Ship);
                return $"{T.ChanceAccept}: {Units.PercentageColored(surrenderChance)}";
            });
        }

        private void OnHailNPC(NPCShip target, ShipHailActionUI ui) {
            NPCShipBehavior_Sector_Combat.OfferSurrender_P2N(ui, target);
        }

        public bool P2N_IsCompatible(NPCShip target) {
            if (!target.IsInCombat) { return false; }
            var enemies = target.SO.S.Query.GetEnemyShips.Ask();
            if (!enemies.Contains(target)) { return false; }
            if (target.Memory.HasCustom) {
                if (target.Memory.TryCustom(
                        NPCShipBehavior_Sector_Combat.Key_Bool_SurrenderOffered,
                            out bool surrenderOffered)) {
                    return !surrenderOffered;
                }
            }
            return true;
        }

        public static void HailPlayer(NPCShip ship) {
            ship.CombatState.IsNegotiating = true;
            ship.Memory.Custom.SetBool(
                NPCShipBehavior_Sector_Combat.Key_Bool_SurrenderOffered, true);
            var act = Registries.ShipHailActions.FastGet(ActIdH);
            ShipHailActionUI.For(ship.SO.S, ship, true, act)?.HailPlayer(act);
        }

        public void N2P_LoadPlayerChoice(ShipHailActionUI actUI, NPCShip caller, Lifetime life, RectTransform container, ref int idx) {
            actUI.IsWaitingForPlayerChoice = true;
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CStarcreds, T.VerbAccept, () => {
                caller.CombatState.IsNegotiating = false;
                NPCShipBehavior_Sector_ExchangeGift.DoStartExchange_N2P(caller, GiftMode.SurrenderRansom);
            });
        }


        public void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship) {
            ui.SetResponse(
                isLastMsg: false,
                ShipChatTopic.SurrenderRequest, ShipChatResponseType.Neutral);
        }

        public void N2P_OnPlayerChoiceTimeout(NPCShip caller) {
            caller.CombatState.IsNegotiating = false;
            // TODO impact relationships
            caller.Memory.Custom.SetBool(
                    NPCShipBehavior_Sector_Combat.Key_Bool_SurrenderOffered, true);
        }
    }
}
