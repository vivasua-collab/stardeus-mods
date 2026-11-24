using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Naming;
using Game.Systems.Story;
using Game.Systems.Notify;

namespace Game.Systems.Story.Events {
    public sealed class EggHatchCatsStoryEvent : EggHatchBaseStoryEvent {
        public const string EventId = "EggHatchCats";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new EggHatchCatsStoryEvent());
        }

        protected override void DoSpawn(StoryContext ctx, StoryEventData data,
                EggComp egg, Vector2 eggPos) {
            var rng = ctx.S.Rng.Fork();
            var numCats = rng.Range(1, 3);
            var cat = Species.Get(SpeciesIdH.Cat);
            Being firstCat = null;
            for (int i = 0; i < numCats; i++) {
                var vars = new GenBeingVars(cat, BeingType.Clone);
                vars.IsClone = true;
                var beingData = ctx.S.Query.GenBeingFull.Ask(vars, rng);
                var cmd = new CmdSpawnBeing(beingData, eggPos
                    + rng.InsideUnitCircle().normalized * 0.25f);
                cmd.Execute(ctx.S);
                if (cmd.SpawnedBeing != null) {
                    BeingUtils.ShowColonyGreetingFor(ctx.S, cmd.SpawnedBeing);
                    firstCat ??= cmd.SpawnedBeing;
                }
            }
            if (firstCat == null) { return; }
            The.AchievementTracker.Unlock(Achievements.AchievementEggCats.AchievementId);
            data.CreateLogEntry(IconId.CPreviewCat01,
                // actual translation is "something"
                T.EvtEggHatchedCrawlersTitle,
                T.BeingsCat01Desc);
            Notification.BuildFor(firstCat.Data, null, data.LogEntry.Title,
                    Consts.TicksPer3Days, NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .Show();
        }
    }
}
