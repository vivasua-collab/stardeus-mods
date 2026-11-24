using Game.Constants;
using Game.Data;
using Game.Expeditions;
using Game.Systems.Creatures;
using Game.Systems.UIUX;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;

namespace Game.Systems.Story {
    // Eligible for both plain POI and Anomaly expedition targets - EventFlags carries both
    // "Expedition" and "Anomaly", and hook matching only requires the requested flags to be a
    // subset of the def's flags (see EventHookStoryAspect.EnqueueHookEvents), so a plain POI
    // hook (Expedition only) still matches this def.
    public class ExpeditionFoundDeserterStoryEvent : ExpeditionStoryEvent {
        public const string EventId = "ExpeditionFoundDeserter";
        public override string Id => EventId;
        public override string Title => T.EvtExpFoundDeserterTitle;
        [SelfInit]
        private static void Register() {
            Register(new ExpeditionFoundDeserterStoryEvent());
        }

        public override bool IsAvailableFor(Expedition exp) {
            return DeserterUtils.HasReturnableDeserter(exp.S);
        }

        private void CreateLogEntry(StoryEventData data) {
            data.CreateLogEntry(IconId.CEnterSpaceObject,
                T.EvtExpFoundDeserterTitle,
                T.EvtExpFoundDeserterGoodTitle);
        }

        public override void TickExpedition(StoryEventData data, Expedition exp) {
            if (!exp.Context.HadActionRequired) {
                exp.Context.SetRequiredAction((exp) => BringDeserterBack(data, exp));
            }
        }

        private void BringDeserterBack(StoryEventData data, Expedition exp) {
            if (IsShowingChoice(data)) { return; }
            SetShowingChoice(data, true);
            WPopup.Create(false,
                GraphicsId.SpaceObjectsDerelictsShipFrigate01_Derelict_R,
                    T.EvtExpFoundDeserterTitle,
                    T.EvtExpFoundDeserterChoice)
                    .WithAction(T.VerbRescue, () => {
                    var ctx = exp.S.Query.GetStoryContext.Ask();
                    var deserter = DeserterUtils.FindReturnableDeserter(exp.S);
                    if (deserter == null) {
                        D.Err("No returnable deserter when resolving ExpeditionFoundDeserter rescue: {0}", data);
                        data.SetState(ctx.S, StoryEventState.Ending);
                        FinalizeEvent(data, exp, false);
                        return;
                    }
                    var evt = StoryEventDef.Get("OnDemand/SpawnDeserterOnShuttleLand");
                    var evtData = StoryEventData.CreateFor(evt);
                    evtData.TargetBeing = deserter;
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
            exp.Tick(G.Ticks);
        }
    }
}
