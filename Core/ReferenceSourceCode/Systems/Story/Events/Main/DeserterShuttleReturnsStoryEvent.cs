using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Notify;
using Game.Utils;

namespace Game.Systems.Story {
    // Ambient positive event: a shuttle a colonist deserted with (and that was never
    // deconstructed - see DeserterUtils.HasReturnableDeserterWithShuttle) flies itself back
    // and lands, bringing the deserter home. All the fanfare happens on arrival (OnTick), not
    // when the flight starts (OnStart) - StoryEventData only allows one log entry.
    public sealed class DeserterShuttleReturnsStoryEvent : StoryEvent {
        public const string EventId = "DeserterShuttleReturns";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DeserterShuttleReturnsStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return DeserterUtils.HasReturnableDeserterWithShuttle(ctx.S, out _, out _);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!DeserterUtils.HasReturnableDeserterWithShuttle(s, out var deserter, out var shuttle)) {
                data.FailEvent(s, "No returnable deserter with a recoverable shuttle", isHardFailure: false);
                return;
            }
            data.TargetBeing = deserter;
            data.TargetTile = shuttle.Tile;

            shuttle.IsLost = false;
            shuttle.HideInfoIcon();
            shuttle.LaunchShuttle(false);
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (data.TargetTile == null || !data.TargetTile.IsActive) {
                data.FailEvent(s, "Shuttle was deconstructed before it could land");
                return;
            }
            var shuttle = data.TargetTile.GetComponent<ShuttleComp>();
            if (shuttle == null || shuttle.IsOut) {
                return;
            }

            var deserter = data.TargetBeing;
            var being = DeserterUtils.ReturnDeserter(s, deserter, shuttle.OutPort, ctx.Ticks);
            if (being == null) {
                data.FailEvent(s, "Failed to respawn returning deserter");
                return;
            }

            var title = T.EvtDeserterShuttleReturnsTitle;
            data.CreateLogEntry(IconId.CPropulsion, title,
                T.EvtDeserterShuttleReturnsDesc_V1.F(Texts.LinkToBeing(deserter)));
            Notification.BuildFor(being.Data, null, data.LogEntry.Desc, Consts.TicksPerDay,
                    NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .Show();
            s.Sig.ShowFanfare.Send(Texts.LinkToTile(shuttle.Tile, Texts.Yellow(title)));
            s.Sys.Log.AddLine(shuttle.Tile, title);

            EntityUtils.CameraFocusOn(being, true);
            data.SetState(s, StoryEventState.Ending);
        }
    }
}
