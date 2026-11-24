using System.Collections;
using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using Game.Story;
using Game.Story.Events;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Story;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionColonize : SOAction {
    public const string ActionId = "Colonize";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionColonize());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            var so = ctx.SO;
            if (so.Type.InheritParentLooks) {
                so = so.Parent;
            }
            if (so.Faction != null) { return false; }
            if (so.Flags.HasFlag(SOFlags.Settled)) { return false; }
            return so.Flags.HasFlag(SOFlags.Habitable);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.ColonizeThisPlanet, BeginColonize);
            return true;
        }

        public void BeginColonize() {
            if (!IsAvailable(ctx)) { return; }
            if (ctx.S.Sys.Ship.IsMoving || ctx.S.Sys.Ship.HasDestination(out _)) {
                ctx.S.Sig.ShowErrorMessage.Send(T.ShipMustBeStoppedFirst);
                return;
            }
            if (!ctx.S.Sys.Terraform.CanBeginSettleIn()) {
                return;
            }
            ctx.S.Sys.Terraform.BeginSettleIn();
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
