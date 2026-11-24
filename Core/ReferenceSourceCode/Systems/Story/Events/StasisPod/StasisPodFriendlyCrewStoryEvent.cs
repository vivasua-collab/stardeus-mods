using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Input;
using Game.Data.Factions;

namespace Game.Systems.Story {
    public sealed class StasisPodFriendlyCrewStoryEvent : StoryEvent {
        public const string EventId = "StasisPodFriendlyCrew";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new StasisPodFriendlyCrewStoryEvent());
        }

        private void CreateLogEntry(StoryEventData data, int count) {
            data.CreateLogEntry(IconId.CPreviewHuman01,
                    T.EvtStasisPodFriendlyCrewTitle,
                    T.EvtStasisPodFriendlyCrewDesc);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            var rng = new Rng(pod.ContentsSeed);
            var spawnPos = EntityUtils.SafeSpawnPosFor(pod.Tile, rng.Fork());

            var numBeings = 1;
            if (rng.Chance(0.1f)) {
                numBeings++;
            }
            if (rng.Chance(0.05f)) {
                numBeings++;
            }

            Being being = null;
            var bodyVars = FactionDef.RandomBodyVars(rng);
            for (int i = 0; i < numBeings; i++) {
                var vars = new GenBeingVars(Species.Get(SpeciesIdH.Human), BeingType.Friendly);
                vars.Body = bodyVars;
                var data2 = BeingUtils.GenBeingWithInventory(vars, rng,
                    GenItemFlags.Casual | GenItemFlags.SpaceReady);
                var b = s.Beings.Create(spawnPos, data2);
                BeingUtils.ShowColonyGreetingFor(ctx.S, b);
                BeingUtils.AddInitialMentalBoostFor(data2, 3);
                being ??= b;
            }

            if (being == null) {
                data.FailEvent(s, "Did not spawn any friendly crew!");
                return;
            }

            pod.Open();
            CreateLogEntry(data, numBeings);

            Notification.BuildFor(pod.Tile, IconId.CPreviewHuman01,
                    data.LogEntry.Title, Consts.TicksPerDay, NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            s.Sig.ShowFanfare.Send(Texts.LinkToTile(pod.Tile,
                Texts.Yellow(T.EvtStasisPodFriendlyCrewTitle)));
            s.Sys.Log.AddLine(pod.Tile, T.EvtStasisPodFriendlyCrewTitle);

            PauseClock();
            EntityUtils.CameraFocusOn(being, true);
        }
    }
}
