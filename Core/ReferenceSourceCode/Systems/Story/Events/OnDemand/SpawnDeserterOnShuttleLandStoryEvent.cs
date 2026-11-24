using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;

namespace Game.Systems.Story {
    // Follow-up to ExpeditionFoundDeserterStoryEvent's rescue choice: waits for the shuttle to
    // actually land back home (mirrors SpawnSurvivorsOnShuttleLandStoryEvent), then revives the
    // specific deserter set on TargetBeing instead of generating a new random being.
    public sealed class SpawnDeserterOnShuttleLandStoryEvent : StoryEvent {
        public const string EventId = "SpawnDeserterOnShuttleLand";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new SpawnDeserterOnShuttleLandStoryEvent());
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.TargetTile == null || !data.TargetTile.IsActive) {
                data.FailEvent(s, "Must have active shuttle set on TargetTile!");
                return;
            }
            if (!data.TargetTile.TryGetComponent<ShuttleComp>(out var shuttle)) {
                data.FailEvent(s, "TargetTile not a shuttle!");
                return;
            }
            if (shuttle.IsOut) { return; }

            var deserter = data.TargetBeing;
            if (deserter == null) {
                data.FailEvent(s, "Must have deserter set on TargetBeing!");
                return;
            }

            var being = DeserterUtils.ReturnDeserter(s, deserter,
                shuttle.Tile.Transform.WorkSpotOrPos, ctx.Ticks);
            if (being == null) {
                data.FailEvent(s, $"Failed spawning returning deserter at shuttle position: {shuttle.Entity.Position}");
                return;
            }

            data.HideLogEntry();
            data.SetState(s, StoryEventState.Ending);
            var title = Texts.Green(T.EvtExpFoundDeserterGoodTitle);
            s.Sys.Log.AddLine(shuttle.Tile, title);
            s.Sig.ShowFanfare.Send(Texts.LinkToTile(shuttle.Tile, title));
        }
    }
}
