using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System.Linq;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionHyperjumpRegionRelay : SOAction {
        public const string ActionId = "HyperjumpRegionRelay";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHyperjumpRegionRelay());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return CanRelayTo(ctx.SO);
        }

        public static bool CanRelayTo(SpaceObject so) {
            if (so?.Region == null) {
                return false;
            }
            // A relay only jumps to another relay: without one on the far side the ship could
            // not get back before it has an FTL drive
            if (!SOUtils.HasHyperjumpRelay(so.Region)) {
                return false;
            }
            var canReach = SOUtils.CanJumpTo(so, out var isWarp);
            return canReach && !isWarp && so.S.Universe.IsUsingHyperspaceRelay;
        }

        public override bool AddOptions(QuickChoice qc) {
            var btn = new QuickChoice.Button() {
                AutoClose = true,
                OnClick = Hyperjump,
                TipFunc = RelayTip,
            };
            qc.AddButton($"{T.TravelHere} ({T.SpaceObjectHyperjumprelay})", btn);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(this, UDBT.DBtn, IconId.CHyperspaceEnter,
        //             $"{T.TravelHere} ({T.SpaceObjectHyperjumprelay})")
        //                 .WithTooltipFunction(RelayTip)
        //                 .WithClickFunction(Hyperjump);
        // }

        private string RelayTip() {
            var relay = A.PlayerSector.Children.FirstOrDefault(
                c => c.Type.IdH == SpaceObjectTypeIdH.HyperjumpRelay);
            if (relay == null) {
                D.Err("RELAY NULL in jump with relay so action! {0}", A.PlayerSector);
                return null;
            }
            var sb = Caches.StringBuilder3;
            sb.Clear();
            SOActionUseRelay.AppendRelayTip(A.S, relay, sb);
            return sb.ToString();
        }

        private void Hyperjump() {
            DoEnqueueRelay(ctx.SO, ctx.S);
        }

        /// <summary>
        /// The gated entry point - also used directly by UISpaceObject's RMB enqueue, so both
        /// the QuickChoice button and RMB show the same abandon-derelicts confirmation.
        /// </summary>
        public static void DoEnqueueRelay(SpaceObject so, GameState s) {
            if (!CanRelayTo(so)) { return; }
            if (!SOUtils.CheckCanQueueHyperjump(s, UniverseLayer.Region)) { return; }

            if (!s.Sys.ShipNav.CanMoveInHyperspace(true,
                    UniverseLayer.Region, out var failReason)) {
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
                var relay = A.PlayerSector.Children.FirstOrDefault(
                    c => c.Type.IdH == SpaceObjectTypeIdH.HyperjumpRelay);
                s.Universe.CurrentRelay = relay;

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
