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
using Game.Systems.UIUX;

namespace Game.Systems.Story {
    public class ExpeditionCrashSiteSurvivorsStoryEvent : ExpeditionStoryEvent {

        public const string EventId = "ExpeditionCrashSiteSurvivors";
        public override string Id => EventId;
        public override string Title => T.EvtExpMerchantTitle;
        [SelfInit]
        private static void Register() {
            Register(new ExpeditionCrashSiteSurvivorsStoryEvent());
        }

        private void CreateLogEntry(StoryEventData data) {
            data.CreateLogEntry(IconId.CEnterSpaceObject,
                T.EvtExpCrashSiteSurvivorsTitle,
                T.EvtExpCrashSiteSurvivorsGoodTitle);
        }

        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction((exp) => BringSurvivorsBack(data, exp));
            }
        }

        private void BringSurvivorsBack(StoryEventData data, Expedition exp) {
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);
            WPopup.Create(false,
                GraphicsId.SpaceObjectsDerelictsShipFrigate01_Derelict_R,
                    T.EvtExpCrashSiteSurvivorsTitle,
                    T.EvtExpCrashSiteSurvivorsChoice)
                    .WithAction(T.VerbRescue, () => {
                    var ctx = exp.S.Query.GetStoryContext.Ask();
                    var evt = StoryEventDef.Get("OnDemand/SpawnSurvivorsOnShuttleLand");
                    var evtData = StoryEventData.CreateFor(evt);
                    evtData.Seed = Hashes.Quick(data.Seed, Primes.Prime022);
                    evtData.TargetTile = exp.Shuttle.Tile;
                    ctx.S.Query.StartStoryEvent.Ask(evtData);
                    data.SetState(ctx.S, StoryEventState.Ending);
                    FinalizeEvent(data, exp, true);
                }).WithAction2(T.VerbLeave, () => {
                    var ctx = exp.S.Query.GetStoryContext.Ask();
                    data.SetState(ctx.S, StoryEventState.Ending);
                    FinalizeEvent(data, exp, false);
                }).Spawn(true);
        }

        private void FinalizeEvent(StoryEventData data, Expedition exp, bool didRescue) {
            if (didRescue) {
                CreateLogEntry(data);
            } else {
                data.HideLogEntry();
            }
            exp.Context.ClearRequiredAction();
            // Force the next phase
            D.Warn("Forcing next phase after event");
            exp.Tick(G.Ticks);
            D.Warn("Expedition should now be in next phase: {0}",
                exp.CurrentStage);
        }
    }
}
