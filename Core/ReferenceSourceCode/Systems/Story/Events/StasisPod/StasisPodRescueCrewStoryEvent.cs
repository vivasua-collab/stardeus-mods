using Game.CodeGen;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Naming;
using Game.Systems.Notify;
using Game.Utils;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Story {
    // Inner "pod opened" event of the second chance, only ever reached via
    // StasisPodComp.RiggedEventId (see the "OnDemand/SecondChancePod" def, which rigs the outer
    // StasisPodStoryEvent to this). SecondChanceStoryAspect decides when it is sent.
    public sealed class StasisPodRescueCrewStoryEvent : StoryEvent {
        public const string EventId = "StasisPodRescueCrew";
        public override string Id => EventId;
        // The StoryEventDef.Id (folder/file path), matching
        // Config/Story/Events/StasisPod/RescueCrew.json - this is what RiggedEventId needs,
        // since StoryEventDef.Get resolves by def Id, not by the C# EventId above.
        public const string DefId = "StasisPod/RescueCrew";

        private const int CrewSize = 2;

        [SelfInit]
        private static void Register() {
            Register(new StasisPodRescueCrewStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return ColonyLifeSigns.IsColonyHelpless(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            // The colony can recover while the pod is falling or during its two hour opening
            // timer. A soft failure here is what makes StasisPodComp fall through to the
            // ordinary StasisPod hook, so the pod still delivers something - just not a crew
            // that is no longer needed.
            if (!ColonyLifeSigns.IsColonyHelpless(s)) {
                data.FailEvent(s, "Colony recovered before the rescue pod opened",
                    isHardFailure: false);
                return;
            }

            var rng = new Rng(pod.ContentsSeed);
            var spawnPos = EntityUtils.SafeSpawnPosFor(pod.Tile, rng.Fork());
            var bodyVars = FactionDef.RandomBodyVars(rng);

            Being first = null;
            for (int i = 0; i < CrewSize; i++) {
                var vars = new GenBeingVars(Species.Get(SpeciesIdH.Human), BeingType.Friendly);
                vars.Body = bodyVars;
                var beingData = BeingUtils.GenBeingWithInventory(vars, rng,
                    GenItemFlags.Casual | GenItemFlags.SpaceReady);
                // Rescuers arriving on a dead ship without the one thing that lets them cross
                // it would defeat the point
                JetSuits.TryEquip(s, beingData, rng);
                var being = s.Beings.Create(spawnPos, beingData);
                BeingUtils.ShowColonyGreetingFor(s, being);
                BeingUtils.AddInitialMentalBoostFor(beingData, 3);
                first ??= being;
            }

            if (first == null) {
                data.FailEvent(s, "Did not spawn any rescue crew!");
                return;
            }

            pod.Open();
            data.CreateLogEntry(IconId.CPreviewHuman01,
                T.EvtRescueCrewTitle, T.EvtRescueCrewDesc);

            Notification.BuildFor(pod.Tile, IconId.CPreviewHuman01,
                    data.LogEntry.Title, Consts.TicksPerDay, NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .WithTooltip(data.LogEntry.Desc)
                .Show();

            s.Sig.ShowFanfare.Send(Texts.LinkToTile(pod.Tile,
                Texts.Yellow(T.EvtRescueCrewTitle)));
            s.Sys.Log.AddLine(pod.Tile, T.EvtRescueCrewTitle);

            PauseClock();
            EntityUtils.CameraFocusOn(first, true);
        }
    }
}
