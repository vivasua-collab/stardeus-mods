using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionHyperjumpRegionFTL : SOAction {
        public const string ActionId = "HyperjumpRegionFTL";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHyperjumpRegionFTL());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so?.Region == null) {
                return false;
            }
            return CanFTLTo(so);
        }

        public static bool CanFTLTo(SpaceObject so) {
            if (so == null) { return false; }
            if (so.Region == null) { return false; }
            var s = so.S;
            var loc = s.Universe.HyperspaceLocation ?? s.Universe.Player.Parent;
            var sector = loc?.Sector;
            if (sector == null) { return false; }
            // check if we have a charged warp drive in current layer
            var state = s.Sys.ShipNav.HyperspaceStateFor(so.Parent);
            return (state.Engines - state.Warps) > 0;
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton($"{T.TravelHere} ({T.ObjectsDevicesFtldrive})", AddToQueue);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(this, UDBT.DBtn, IconId.CHyperspaceEnter,
        //             $"{T.TravelHere} ({T.ObjectsDevicesFtldrive})")
        //                 .WithClickFunction(AddToQueue);
        // }

        private void AddToQueue() {
            DoEnqueueFTL(ctx.SO, ctx.S);
        }

        public static void DoEnqueueFTL(SpaceObject so, GameState s) {
            if (!CanFTLTo(so)) { return; }
            if (!SOUtils.CheckCanQueueHyperjump(s, UniverseLayer.Universe)) { return; }

            if (!s.Sys.ShipNav.CanMoveInHyperspace(true,
                    UniverseLayer.Universe, out var failReason)) {
                WPopup.Warning(T.Warning, $"{T.EnterHyperspaceFailed}:\n{failReason}");
            }

            if (A.IsInHyperspace) {
                DoEnqueue(so, s);
            } else {
                SOUtils.CheckAbandonDerelicts(() => DoEnqueue(so, s));
            }
        }

        private static void DoEnqueue(SpaceObject so, GameState s) {
            var res = s.Query.AddToHyperjumpQueue.Ask(so.SpaceEntity);
            if (res.IsSuccess) {

            } else {
                if (res.FailReason != null) {
                    s.Sig.ShowErrorMessage.Send(res.FailReason);
                } else {
                    UISounds.PlayActionDenied();
                }
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
