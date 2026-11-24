using System.Collections;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Systems.Quests;
using Game.Systems.Story;
using Game.UI;
using Game.Utils;
using KL.Collections;
using KL.Utils;
using UnityEngine;
using Game.Systems.UIUX;

namespace Game.Data.Space {
    public sealed class SOActionInvestigate : SOAction {
        public const string ActionId = "Investigate";
        [SelfInit]
        private static void Register() {
            Register(ActionId, new SOActionInvestigate());
        }

        public override bool IsAvailable(SOActionContext ctx) {
            return ctx.SO.Type.IdH == SpaceObjectTypeIdH.Signal
                && ctx.S.Sys.Ship.IsNearby(ctx.SO)
                && SOUtils.IsQuestTarget(ctx.SO);
        }

        public override bool AddOptions(QuickChoice qc) {
            qc.AddButton(T.VerbInvestigate, ActivateSignal);
            return true;
        }

        // public override UDB GetUIBlock() {
        //     return dataBlock ??= UDB.Create(
        //         this, UDBT.DBtn, IconId.CMagnifier,
        //             T.VerbInvestigate).WithClickFunction(ActivateSignal);
        // }

        private void ActivateSignal() {
            if (!IsAvailable(ctx)) {
                UISounds.PlayActionDenied();
                return;
            }
            if (!ctx.S.Sys.Ship.HasCommunicator()) {
                ctx.S.Sig.ShowErrorMessage.Send(T.CommunicatorMissing);
                return;
            }

            var so = ctx.SO;
            var s = ctx.S;
            DoActivateSignal(so, s);
        }

        public static void DoActivateSignal(SpaceObject so, GameState s) {
            var questTargets = s.Query.GetQuestStepTargets.Ask(
                SOUtils.QuestProviderFor(so));
            bool keepSO = false;
            if (questTargets.Length > 0) {
                for (int i = 0; i < questTargets.Length; i++) {
                    var target = questTargets[i];
                    keepSO |= target.KeepSOOnInteract;
                    target.OnInteract?.Invoke();
                }
            }
            if (!keepSO) {
                SOUtils.RemoveSO(so);
            }
        }

        public override string Id => ActionId;

        public override int UIPriority => 50;
    }
}
