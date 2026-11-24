using System.Collections;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Ships;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Data.Space {
    public sealed class SOActionHail : SOAction {
        public const string ActionId = "Hail";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHail());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (ctx.S.Clock.IsForceStopped) { return false; }
            if (ctx.SO.Parent != ctx.Player.Parent) { return false; }
            return ctx.SO.Ship?.IsPlayer == false && !ctx.SO.Ship.IsDerelict;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.VerbHail, Hail);
            return true;
        }

        private void Hail() {
            if (!IsAvailable(ctx)) {
                UISounds.PlayActionDenied();
                return;
            }
            var s = ctx.S;
            var ship = ctx.SO.Ship;
            HailShip(s, ship);
        }

        public static void HailShip(GameState s, NPCShip ship) {
            if (!s.Sys.Ship.HasCommunicator()) {
                s.Sig.ShowErrorMessage.Send(T.CommunicatorMissing);
                return;
            }
            var hailUI = ShipHailActionUI.For(s, ship, false, null);
            if (hailUI != null) {
                hailUI.ShowHailPanel();
            } else {
                D.Err("Hail ui failed to create");
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
