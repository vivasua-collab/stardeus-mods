using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public class ExpeditionPickUpCrawlersStoryEvent : ExpeditionStoryEvent {
        public const string EventId = "ExpeditionPickUpCrawlers";
        public override string Id => EventId;
        public override string Title => null;

        [SelfInit]
        private static void Register() {
            Register(new ExpeditionPickUpCrawlersStoryEvent());
        }

        public override bool IsAvailableFor(Expedition exp) {
            return exp.Target.CanHaveLife;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var exp = GetExpedition(ctx, data);
            var evt = StoryEventDef.Get("OnDemand/SpawnCrawlerOnShuttleLand");
            var evtData = StoryEventData.CreateFor(evt);
            evtData.TargetTile = exp.Shuttle.Tile;
            evtData.Seed = data.Seed;
            ctx.S.Query.StartStoryEvent.Ask(evtData);
            data.HideLogEntry();
            data.SetState(ctx.S, StoryEventState.Ending);
        }

        public override void TickExpedition(StoryEventData data, Expedition exp) {
        }
    }
}
