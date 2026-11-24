using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;
using Game.Input;

namespace Game.Data.Space {
    public sealed class SOActionHyperjumpEnqueue : SOAction {
        public const string ActionId = "HyperjumpEnqueue";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHyperjumpEnqueue());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so.SpaceEntity == null) {
                return false;
            }
            if (so == null || so.Region != null || so == A.PlayerSector) {
                return false;
            }
            var queue = ctx.S.Query.GetHyperjumpQueue.Ask();
            var q = queue.Queue;

            return !q.Contains(so.SpaceEntity);
        }

        public override bool AddOptions(QuickChoice qc) {
            var glyph = The.Bindings.GetControlName(ActionType.ClickCancel);
            var name = $"{T.TravelHere} {Texts.WithSize(glyph, 75)} {Texts.GreyParentheses(T.VerbHyperjump)}";
            qc.AddButton(name, Enqueue);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(this, UDBT.DBtn, IconId.CHyperspaceEnter,
        //             $"{T.TravelHere} ({T.VerbHyperjump})")
        //                 .WithClickFunction(Enqueue);
        // }

        private void Enqueue() {
            if (!IsAvailable(ctx)) { return; }
            DoEnqueue(ctx.SO, ctx.S);
        }

        /// <summary>
        /// The gated entry point - also used directly by UISpaceObject's RMB enqueue, so both
        /// the "Travel Here" menu button and RMB show the same abandon-derelicts confirmation.
        /// </summary>
        public static void DoEnqueue(SpaceObject so, GameState s) {
            if (A.IsInHyperspace) {
                AddToQueue(so, s);
            } else {
                if (!SOUtils.CheckCanQueueHyperjump(s, so.Parent.Layer)) { return; }
                SOUtils.CheckAbandonDerelicts(() => AddToQueue(so, s));
            }
        }

        private static void AddToQueue(SpaceObject so, GameState s) {
            var res = s.Query.AddToHyperjumpQueue.Ask(so.SpaceEntity);
            if (res.IsSuccess) {


                if (!s.Sys.ShipNav.CanMoveInHyperspace(true,
                        so.Parent.Layer, out var failReason)) {
                    WPopup.Warning(T.OperationFailed, null, $"{T.EnterHyperspaceFailed}:\n{failReason}");
                }

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
