using System.Collections;
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionStartExpedition : SOAction {
        public const string ActionId = "StartExpedition";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionStartExpedition());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (!so.Type.CanBeExplored) {
                return false;
            }
            if (ctx.S.Sys.Expedition.HasExpeditionTo(so)) {
                return false;
            }
            // return so.IsReachableFrom(ctx.S.Universe.Player.Location,
            //         Tunable.Float(TunableKey.ExpeditionReachDistanceMul));
            // return ctx.S.Sys.Ship.IsNearby(so);
            return true;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddIconButton(GraphicsId.ObjectsDevicesShuttleFlying_R, T.LaunchExpedition, BeginEnter);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return UDB.Create(this, UDBT.DBtn, IconId.CPickaxe,
        //         T.LaunchExpedition)
        //             .WithClickFunction(BeginEnter);
        // }

        private void BeginEnter() {
            if (ctx.S.GameId != The.R.State.GameId) {
                D.Err("game id is wrong! {0} != {1}", ctx.S.GameId,
                    The.R.State.GameId);
            }
            if (!IsAvailable(ctx)) { return; }
            if (ctx.S.Components.CountOfConstructed<ShuttleComp>(1) == 0) {
                UISounds.PlayActionDenied();
                ctx.S.Sig.ShowErrorMessage.Send(T.NoShuttlesDesc);
                return;
            }
            var so = ctx.SO;
            ctx.S.UIQuery.StartExpeditionTo.Ask(so);
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
