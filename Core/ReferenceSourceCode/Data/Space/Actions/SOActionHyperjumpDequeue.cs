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
    public sealed class SOActionHyperjumpDequeue : SOAction {
        public const string ActionId = "HyperjumpDequeue";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionHyperjumpDequeue());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so.SpaceEntity == null) {
                return false;
            }
            var queue = ctx.S.Query.GetHyperjumpQueue.Ask();
            var q = queue.Queue;
            return q.Contains(so.SpaceEntity);
        }

        public override bool AddOptions(QuickChoice qc) {
            var glyph = The.Bindings.GetControlName(ActionType.ClickCancel);
            var name = $"{T.RemoveFromQueue} {Texts.WithSize(glyph, 75)}";
            qc.AddButton(name, Dequeue);
            return true;
        }

        private void Dequeue() {
            if (!IsAvailable(ctx)) { return; }
            var so = ctx.SO;
            var s = ctx.S;
            var res = s.Query.RemoveFromHyperjumpQueue.Ask(so.SpaceEntity);
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
