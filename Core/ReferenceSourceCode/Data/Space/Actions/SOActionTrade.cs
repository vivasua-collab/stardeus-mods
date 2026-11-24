using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data.Economy;
using Game.Data.Planets;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using System;
using Game.Input;
using Game.Systems.Trade;
using Game.Systems.UIUX;
using System.Text;
using Game.Components;

namespace Game.Data.Space {
    public sealed class SOActionTrade : SOAction {
        public const string ActionId = "Trade";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionTrade());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.SO.IsShip) {
                // ships trade through hailing
                return false;
            }
            if (ctx.SO.TraderId != 0) {
                return true;
            }
            if (ctx.SO.Type.HasTrader) {
                return ctx.S.Sys.Ship.IsNearby(ctx.SO);
            }
            return false;
        }

        public string MerchantFullTooltip() {
            return ctx.SO.Trader.FullTooltip(ctx.S);
        }

        public override bool AddOptions(QuickChoice qc) {
            if (!ctx.S.Sys.Ship.IsNearby(ctx.SO)) {
                // Not nearby - no trade button
                return false;
            }
            var btn = new QuickChoice.Button() {
                AutoClose = true,
                OnClick = InitiateTrade,
                TipFunc = MerchantFullTooltip,
            };
            qc.AddButton(T.Trade, btn);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     if (!ctx.S.Sys.Ship.IsNearby(ctx.SO)) {
        //         var trader = ctx.SO.Trader;
        //         return UDB.Create(this, UDBT.DText, IconId.CStarcreds,
        //                 trader.Name)
        //             .WithTooltipFunction(MerchantFullTooltip);
        //     }
        //     return dataBlock ??= UDB.Create(this, UDBT.DBtn, IconId.CTrade,
        //             T.Trade)
        //             .WithTooltipFunction(MerchantFullTooltip)
        //             .WithClickFunction(InitiateTrade);
        // }

        public void InitiateTrade() {
            StringBuilder sb = null;
            if (!A.HasCommunicator) {
                sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendFormat("{0}:", T.TradeFailed);
                sb.AppendFormat("\n - {0}", Texts.Red(T.CommunicatorMissing));
            }
            if (TradeUI.FindTradePortal(ctx.S) == null) {
                if (sb == null) {
                    sb = Caches.StringBuilder;
                    sb.Clear();
                    sb.AppendFormat("{0}:", T.TradeFailed);
                }
                sb.AppendFormat("\n - {0}", TradePortalComp.NotAvailable);
            }
            if (sb != null) {
                ctx.S.Sig.ShowErrorMessage.Send(sb.ToString());
                return;
            }
            if (ctx.SO.Trader == null) {
                D.Err("No trader on SO when starting a trade: {0}", ctx.SO);
                UISounds.PlayActionDenied();
                return;
            }
            if (!ctx.SO.Trader.WillTradeWith(A.Player.Ship, out var reason)) {
                var sb2 = Caches.StringBuilder;
                sb2.Clear();
                sb2.AppendLine(T.TradeRefused);
                switch (reason) {
                    case TradeRefuseReason.Outlaw: {
                        sb2.AppendFormat(": {0}", Texts.Red(T.ShipViolatesClassLimit));
                        break;
                    }
                    default:
                        D.Err("Unhandled trade refusal reason: {0}", reason);
                        break;
                }
                ctx.S.Sig.ShowErrorMessage.Send(sb2.ToString());
                return;
            }
            var ui = ctx.S.Query.StartTrade.Ask(ctx.SO.Trader);
            if (ui == null) {
                D.Err("Failed to start a trade with space object: {0}", ctx.SO);
                UISounds.PlayActionDenied();
            } else {

            }
        }

        public override string Id => ActionId;
    }
}
