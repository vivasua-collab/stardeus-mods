using System.Collections;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.UI;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

using Game.Utils;
namespace Game.Data.Space {
    public sealed class SOActionRetrieveMiner : SOAction {
        public const string ActionId = "RetrieveMiner";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionRetrieveMiner());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            if (!ctx.S.BB.Get(BBKeyH.MiningAuto, false) && !A.InstantBuild) {
                return false;
            }
            return ctx.S.Sys.Copters.CanRetrieveMinerFrom(ctx.SO);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddIconButton(IconId.CCopter, T.RetrieveResourceMiner, RetrieveMiner);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(
        //         this, UDBT.DBtn, IconId.CDrill,
        //             T.RetrieveResourceMiner).WithClickFunction(RetrieveMiner);
        // }

        private void RetrieveMiner() {
            if (!ctx.S.Sys.Copters.TryOrderMinerRetrievalFrom(ctx.SO)) {
                UISounds.PlayActionDenied();
                if (ctx.UISO != null) {

                }
                return;
            }

            UISounds.PlayDigitalConfirm();
        }

        public override string Id => ActionId;

        public override int UIPriority => 179;
    }
}
