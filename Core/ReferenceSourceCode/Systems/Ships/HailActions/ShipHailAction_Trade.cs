using System;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class ShipHailAction_Trade : ShipHailAction, IShipHailAction_P2N,
            IShipHailAction_N2P {

        public const string ActId = "Trade";
        public override string Id => ActId;
        public static readonly int ActIdH = Hashes.S(ActId);

        [SelfInit]
        private static void Register() {
            Register(new ShipHailAction_Trade());
        }

        public void P2N_LoadHailNPCBlock(ShipHailActionUI ui, NPCShip target, Lifetime life, RectTransform container, ref int idx) {
            if (IsAttackingUs(target)) { return; }
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CStarcreds, T.Trade, () => {
                if (TradeUI.FindTradePortal(ui.S) == null) {
                    ui.S.Sig.ShowErrorMessage.Send($"{T.TradeFailed}: {TradePortalComp.NotAvailable}");
                    return;
                }
                OnHailNPC(target, ui);
            });
            UIUtils.AddTipFunc(life, btn, () => ShowTraderTooltip(target));
        }

        private string ShowTraderTooltip(NPCShip ship) {
            var traderTip = ship.SO.Trader.FullTooltip(ship.SO.S);
            if (ship.Class.IsSpaceStation) {
                return traderTip;
            }
            var chance = CalculateChanceFor(ship);
            var tip = $"{T.ChanceAccept}: {Units.PercentageColored(chance)}";
            tip = $"{tip}<br><br>{traderTip}";
            return tip;
        }


        private void OnHailNPC(NPCShip target, ShipHailActionUI ui) {
            if (IsAttackingUs(target)) {
                ui.SetResponse(
                    isLastMsg: true,
                    ShipChatTopic.Generic, ShipChatResponseType.Negative);
                return;
            }
            if (target.Class.IsSpaceStation) {
                TryTrade(target, ui);
                return;
            }
            float chance = CalculateChanceFor(target);
            if (CheckActionPossibility(target, ui, ShipChatTopic.TradeResponse,
                    target.SeededRng(Primes.Prime052), chance)) {
                TryTrade(target, ui);
            }
        }

        private static float CalculateChanceFor(NPCShip target) {
            Curve curve = Tunables.NPCShipTrade_AttitudeChance;
            var chance = curve.Evaluate(target.AttitudeTowardsPlayer);
            return chance;
        }

        private static void TryTrade(NPCShip target, ShipHailActionUI ui) {
            target.NearbyPosFrom(A.Player.Location, out var isNear);
            if (isNear) {
                NPCShipBehavior_Sector_TradeWith.DoStartTrade(target, A.Player);
                ui.HideHailPanel();
            } else {
                if (target.NumThrustersActual > 0) {
                    NPCShipBehavior_Sector_TradeWith.AssignTo(target, A.Player, true);
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.TradeResponse, ShipChatResponseType.Positive);
                } else {
                    ui.SetResponse(
                        isLastMsg: false,
                        ShipChatTopic.TooFar, ShipChatResponseType.Negative);
                }
            }
        }

        public bool P2N_IsCompatible(NPCShip target) {
            return target.WillTrade && !target.IsInCombat;
        }

        public static void HailPlayer(NPCShip ship) {
            var act = Registries.ShipHailActions.FastGet(ActIdH);
            ShipHailActionUI.For(ship.SO.S, ship, true, act)?.HailPlayer(act);
        }

        public void N2P_LoadPlayerChoice(ShipHailActionUI ui, NPCShip caller, Lifetime life, RectTransform container, ref int idx) {
            ui.IsWaitingForPlayerChoice = true;
            D.Warn("Loading trade choice");
            var btn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, container, idx++);
            btn.Load(IconId.CStarcreds, T.Trade, () => {
                NPCShipBehavior_Sector_TradeWith.DoStartTrade(caller, A.Player);
            });
        }

        public void N2P_OnHailPlayer(ShipHailActionUI ui, NPCShip ship) {
            ui.SetResponse(
                isLastMsg: false,
                ShipChatTopic.TradeRequest, ShipChatResponseType.Neutral);
        }

        public void N2P_OnPlayerChoiceTimeout(NPCShip caller) {
            // TODO impact relationships
            caller.ChangeBehavior(NPCShipBehaviorScope.Sector, false);
        }
    }
}
