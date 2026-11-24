using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System;
using Game.Data.Economy;
using Game.Input;
using Game.Systems.UIUX;
using System.Text;

namespace Game.Data.Space {
    public sealed class SOActionUseRelay : SOAction {
        public const string ActionId = "UseRelay";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionUseRelay());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            return IsAvailable(so);
        }

        public static bool IsAvailable(SpaceObject so) {
            if (so.Type.IdH != SpaceObjectTypeIdH.HyperjumpRelay) {
                return false;
            }
            return so.S.Sys.Ship.IsNearby(so);
        }

        public override bool AddOptions(QuickChoice qc) {
            var btn = new QuickChoice.Button() {
                AutoClose = true,
                OnClick = UseRelay,
                TipFunc = ShowRelayTip,
            };
            qc.AddButton(T.VerbHyperjump, btn);
            return true;
        }

        private string ShowRelayTip() {
            var sb = Caches.StringBuilder;
            sb.Clear();
            AppendRelayTip(ctx.S, ctx.SO, sb);
            return sb.ToString();
        }

        private void UseRelay() {
            if (!IsAvailable(ctx)) { return; }
            var cost = SOUtils.HyperRelayToll(ctx.S, ctx.SO, out _, out _, out _);
            if (!ctx.S.Wallet.CanAfford(cost)) {
                ctx.S.Sig.ShowErrorMessage.Send($"{T.CannotAfford}: {T.SpaceObjectHyperjumprelay} {GlyphMap.ArrowRight} {Texts.Red(Wallet.ToValueString(cost))}");
                return;
            }
            if (!DoUseRelay(ctx.S, ctx.SO, out var failReason)) {
                ctx.S.Sig.ShowErrorMessage.Send(failReason);
            }
        }

        public static void AppendRelayTip(GameState s, SpaceObject relay, StringBuilder sb) {
            var cost = SOUtils.HyperRelayToll(s, relay, out var baseCost,
                out var taxPct, out var discountPercent);
            sb.Clear();
            sb.AppendFormat("{0}: {1}\n", T.NounToll, Texts.Accented(Wallet.ToValueString(baseCost), bold: false));
            if (cost <= 0f) {
                return;
            }
            sb.AppendFormat("{0} ({1}): {2}\n", T.NounFactionTax,
                Texts.Accented(Texts.LinkToFactionTooltip(relay.Faction), bold: false),
                Units.PercentageColoredInverse(taxPct, 0.15f, 0.5f));
            sb.AppendFormat("{0}: {1}\n", T.NounRegionDiscount,
                Texts.Green(Units.Percentage(discountPercent)));
            sb.AppendFormat("{0}: {1}\n", T.NounFinalPrice,
                Texts.Accented(Wallet.ToValueString(cost)));
            sb.AppendFormat("{0}: ", T.OurWallet);
            if (s.Wallet.CanAfford(cost)) {
                sb.AppendLine(Texts.Green(s.Wallet.ValueString));
            } else {
                sb.AppendLine(Texts.Red(s.Wallet.ValueString));
            }
        }

        public static void ShowCannotAffordPopup(GameState s, SpaceObject relay) {
            D.Warn("Can't afford jump: {0}", relay.ParentPath(false));
            var sb = Caches.StringBuilder2;
            AppendRelayTip(s, relay, sb);
            WPopup.Warning($"{T.CannotAfford}: {T.SpaceObjectHyperjumprelay}", sb.ToString());
            sb.Clear();
        }

        public static bool DoUseRelay(GameState s, SpaceObject relay, out string failReason) {
            if (s.Sys.ShipNav.CanEnterHyperspaceNow(false,
                    out failReason, UniverseLayer.Region)) {
                // s.Universe.IsUsingHyperspaceRelay = true;
                // show relay region
                s.Sys.Universe.ShowInStarmap(relay.Parent.Parent);
                // s.Universe.CurrentRelay = relay;
                // s.Sys.ShipNav.SetWantHyperspace(true);
                // The map opens on our own region, which has no travel options - make sure
                // the neighbouring regions offer the relay jump
                s.Universe.CurrentRelay = relay;
                s.Sig.ShowFanfare.Send($"{T.SpaceObjectHyperjumprelay}: {Texts.Green(Texts.LinkToStarmap(relay.Parent.Parent, T.Ready))}");
                return true;
            }
            return false;
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
