using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Notify;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Story {
    // Inner "pod opened" event, only ever reached via StasisPodComp.RiggedEventId (see the
    // "Space/StasisPodDeserterReturn" def, which rigs the outer StasisPodStoryEvent to this).
    public sealed class StasisPodDeserterReturnsStoryEvent : StoryEvent {
        public const string EventId = "StasisPodDeserterReturns";
        public override string Id => EventId;
        // The StoryEventDef.Id (folder/file path) for this event's def, matching
        // Config/Story/Events/StasisPod/DeserterReturns.json - this is what RiggedEventId
        // needs (StoryEventDef.Get resolves by def Id, not by the C# EventId above).
        public const string DefId = "StasisPod/DeserterReturns";
        [SelfInit]
        private static void Register() {
            Register(new StasisPodDeserterReturnsStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return DeserterUtils.HasReturnableDeserter(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            var deserter = DeserterUtils.FindReturnableDeserter(s);
            if (deserter == null) {
                data.FailEvent(s, "No returnable deserter for the rigged stasis pod", isHardFailure: false);
                return;
            }

            var rng = new Rng(pod.ContentsSeed);
            var spawnPos = EntityUtils.SafeSpawnPosFor(pod.Tile, rng.Fork());
            var being = DeserterUtils.ReturnDeserter(s, deserter, spawnPos, ctx.Ticks);
            if (being == null) {
                data.FailEvent(s, "Failed to respawn returning deserter from stasis pod");
                return;
            }

            pod.Open();
            data.CreateLogEntry(IconId.CPreviewHuman01,
                T.EvtStasisPodDeserterReturnsTitle,
                T.EvtStasisPodDeserterReturnsDesc_V1.F(Texts.LinkToBeing(deserter)));

            Notification.BuildFor(pod.Tile, IconId.CPreviewHuman01,
                    data.LogEntry.Title, Consts.TicksPerDay, NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            s.Sig.ShowFanfare.Send(Texts.LinkToTile(pod.Tile,
                Texts.Yellow(T.EvtStasisPodDeserterReturnsTitle)));
            s.Sys.Log.AddLine(pod.Tile, T.EvtStasisPodDeserterReturnsTitle);

            PauseClock();
            EntityUtils.CameraFocusOn(being, true);
        }
    }
}
