using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Utils;
using Game.CodeGen;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.BodyParts;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.Systems.Conditions;
using Game.Systems.Naming;
using Game.Systems.Story;
using Game.Systems.Notify;

namespace Game.Systems.Story.Events {
    public sealed class EggHatchHumanStoryEvent : EggHatchBaseStoryEvent {
        public const string EventId = "EggHatchHuman";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new EggHatchHumanStoryEvent());
        }

        protected override void DoSpawn(StoryContext ctx, StoryEventData data, EggComp egg, Vector2 eggPos) {
            var species = Species.Get(SpeciesIdH.Human);
            var vars = new GenBeingVars(species, BeingType.Clone);
            vars.IsClone = true;
            var beingData = ctx.S.Query.GenBeingFull.Ask(vars, Rng.Unseeded);
            beingData.Body.Flags |= BodyFlags.Clone;
            var human = ctx.S.Beings.Create(eggPos, beingData);
            The.AchievementTracker.Unlock(Achievements.AchievementEggHuman.AchievementId);

            float chanceInfected = 0.5f;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) {
                chanceInfected = 1f;
            }
            #endif
            if (ctx.S.Rng.Chance(chanceInfected)) {
                ConditionState.CreateFor(ctx.S, ConditionIdH.CrawlerInfected,
                    beingData, Severity.Irrelevant, true);
            }

            data.CreateLogEntry(IconId.CPreviewHuman01,
                T.EvtEggHatchedCrawlersTitle, T.BeingsHuman01Desc);
            BeingUtils.ShowColonyGreetingFor(ctx.S, human);
            Notification.BuildFor(human.Data, null, data.LogEntry.Title,
                    Consts.TicksPer3Days, NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .Show();
        }
    }
}
