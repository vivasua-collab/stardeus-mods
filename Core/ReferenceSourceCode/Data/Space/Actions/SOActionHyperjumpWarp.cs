using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Space;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionHyperjumpWarp : SOAction {
        public const string ActionId = "HyperjumpWarp";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHyperjumpWarp());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so == null || so == A.PlayerSector || so.Region == A.PlayerSector.Parent?.Region) {
                return false;
            }
            return CanWarpTo(so, out _);
        }

        public static bool CanWarpTo(SpaceObject so, out int warpsAvailable) {
            warpsAvailable = 0;
            if (so == null) { return false; }
            if (so.Sector == null && so.Region == null) { return false; }
            var s = so.S;
            var loc = s.Universe.HyperspaceLocation ?? s.Universe.Player.Parent;
            var sector = loc?.Sector;
            if (sector == null) { return false; }
            // check if we have a warp drive for this jump's layer
            var state = s.Sys.ShipNav.HyperspaceStateForLayer(SOUtils.JumpLayerFor(so));
            warpsAvailable = state.Warps;
            return state.EnginesWarp > 0;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton($"{T.TravelHere} {Texts.GreyParentheses(T.VerbWarp)}", WarpJump);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(this, UDBT.DBtn, IconId.CHyperspaceEnter,
        //             $"{T.TravelHere} ({T.VerbWarp})")
        //                 .WithClickFunction(WarpJump);
        // }

        private void WarpJump() {
            if (!IsAvailable(ctx)) { return; }
            var so = ctx.SO;
            var s = ctx.S;
            var layer = SOUtils.JumpLayerFor(so);
            if (!SOUtils.CheckCanQueueHyperjump(s, layer)) { return; }

            if (!s.Sys.ShipNav.CanMoveInHyperspace(true,
                    layer, out var failReason)) {
                WPopup.Warning(T.Warning, $"{T.EnterHyperspaceFailed}:\n{failReason}");
            }

            if (A.IsInHyperspace) {
                s.Sig.ShowErrorMessage.Send(T.ExitHyperspaceFirst);
            } else {
                SOUtils.CheckAbandonDerelicts(() => DoEnqueue(so, s));
            }
        }

        private static void DoEnqueue(SpaceObject so, GameState s) {
            var res = s.Query.SetWarpTarget.Ask(so.SpaceEntity);
            if (res.IsSuccess) {

                return;
            }
            if (res.FailReason != null) {
                s.Sig.ShowErrorMessage.Send(res.FailReason);
            } else {
                UISounds.PlayActionDenied();

            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
