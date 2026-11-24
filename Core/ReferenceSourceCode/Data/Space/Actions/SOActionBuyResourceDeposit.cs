using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using KL.Randomness;
using Game.Data.Economy;
using Game.Data.Factions;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionBuyResourceDeposit : SOAction {
        public const string ActionId = "BuyResourceDeposit";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionBuyResourceDeposit());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            // no faction or player faction?
            if (ctx.SO.Faction?.IsPlayer != false) { return false; }

            if (ctx.S.Sys.Planets.IsMinableResource(ctx.SO, out _, out var mat)) {
                return true;
            }
            return false;
        }

        private double value;

        public override bool AddOptions(QuickChoice qc) {
            if (!ctx.S.Sys.Planets.IsMinableResource(ctx.SO, out var idx, out var mat)) {
                return false;
            }
            double valueOfOne;
            var mi = ctx.S.Market.Find(mat.Def.IdH);
            var rng = new Rng(Hashes.Quick(ctx.SO.Seed, Primes.Prime030));
            if (mi == null) {
                float mul;
                switch (mat.MatRarity) {
                    case 2 : mul = 2f; break;
                    case 3 : mul = 10f; break;
                    case 4 : mul = 50f; break;
                    default: mul = 1f; break;
                }
                valueOfOne = rng.Range(50f, 150f) * mul;
            } else {
                valueOfOne = mi.Adjusted().Value;
            }
            double valueOfDeposit = valueOfOne * rng.RangeSmooth(100, 300);
            value = valueOfDeposit;

            qc.AddButton($"{T.Buy} ({Wallet.ToValueString(valueOfDeposit)})", BuyDeposit);
            return true;
            // return UDB.Create(
            //     this, UDBT.DTextBtn, IconId.CStarcreds,
            //         Wallet.ToValueString(valueOfDeposit))
            //             .WithText2(T.Buy)
            //             #if !PRODUCTION_BUILD
            //             .WithTooltipFunction((b) => {
            //                 if (ctx.S.Sys.Planets.TryGetResource(idx, out var resource)) {
            //                     return D.DumpInstance(resource);
            //                 }
            //                 return "nope";
            //             })
            //             #endif
            //             .WithClickFunction(BuyDeposit);
        }

        private void BuyDeposit() {
            FactionDef prevOwner = ctx.SO.Faction;
            if (prevOwner?.IsPlayer != false) {
                UISounds.PlayActionDenied();
                return;
            }
            if (value == 0) {
                UISounds.PlayActionDenied();
                return;
            }
            if (ctx.S.Wallet.TryPay(value, null)) {
                ctx.SO.SetFaction(FactionDef.Player);
                if (ctx.S.Sys.Planets.TryGetResourceForUpdate(ctx.SO,
                        out var idx, out var resource)) {
                    resource.FactionIdH = FactionDef.Player.IdH;
                    ctx.S.Sys.Planets.UpdateResource(idx, in resource);
                }
                var rng = new Rng(Hashes.Quick(ctx.SO.Seed, Primes.Prime030));
                var change = rng.Range(0.03f, 0.05f);
                ctx.S.Query.ChangeFactionAttitude.Ask(prevOwner,
                    FactionDef.Player, change);

                UISounds.PlayDigitalConfirm();
            } else {
                ctx.S.Sig.ShowErrorMessage.Send(T.CannotAfford);
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
