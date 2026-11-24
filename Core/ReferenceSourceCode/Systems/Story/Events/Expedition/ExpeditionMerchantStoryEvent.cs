using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Data.Space;
using Game.Expeditions;
using Game.Procgen.Space;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Trade;
using Game.Data.Factions;
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class ExpeditionMerchantStoryEvent : ExpeditionStoryEvent {

        public const string EventId = "ExpeditionMerchant";
        public const string Var_MerchantNA = "exp.merchant.na";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new ExpeditionMerchantStoryEvent());
        }

        public override string Title => T.EvtExpMerchantTitle;

        public override bool IsAvailableFor(Expedition exp) {
            if (exp.Target.Type.IdH != SpaceObjectTypeIdH.PlanetPOI) {
                return false;
            }
            if (exp.Target.Parent.Type.IdH != SpaceObjectTypeIdH.Planet) {
                return false;
            }
            var data = exp.Target.Data;
            if (data.GetBool("exp.merchant.na", false)) { return false; }
            // var rng = new Rng(Hashes.Quick(exp.Seed, Primes.Prime057));
            if (!exp.Target.CanHaveLife) {
                data.SetBool("exp.merchant.na", true);
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            data.CreateLogEntry(IconId.CTrade, T.EvtExpMerchantTitle, T.EvtExpMerchantDesc);
            // .Target.Data.SetBool(Var_MerchantNA, true);
        }


        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction((e) => MaybeDoTrade(data, e));
            }
        }

        private void MaybeDoTrade(StoryEventData data, Expedition exp) {
            if (data.TempObj is TradeUI ongoingTrade) {
                UIUtils.CloseFullScreenUI();
                ongoingTrade.Show();
                return;
            }
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);
            WPopup.Create(false, IconId.CTrade, T.EvtExpMerchantTitle,
                    T.EvtStasisPodMerchantDesc)
                .WithAction(T.Trade, () => {
                    var rng = new Rng(exp.Seed);
                    var faction = FactionDef.FastGet(FactionIdH.Trader);
                    ongoingTrade = exp.S.Query.CreateRandomTrade.Ask(rng, faction);
                    ongoingTrade.OnEnd = (ui) => FinalizeEvent(data, exp);
                }).WithAction2(T.VerbLeave, () => FinalizeEvent(data, exp))
                .Spawn(true);
        }

        private void FinalizeEvent(StoryEventData data, Expedition exp) {
            exp.Context.ClearRequiredAction();
            data.SetState(exp.S, StoryEventState.Ending);
        }

    }
}
