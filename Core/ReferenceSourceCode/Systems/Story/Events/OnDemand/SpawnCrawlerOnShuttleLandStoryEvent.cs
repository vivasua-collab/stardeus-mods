using System.Collections.Generic;
using Game.Achievements;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using Game.Systems.Naming;

namespace Game.Systems.Story {
    public sealed class SpawnCrawlerOnShuttleLandStoryEvent : StoryEvent {
        public const string EventId = "SpawnCrawlerOnShuttleLand";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new SpawnCrawlerOnShuttleLandStoryEvent());
        }

        public override void OnTick(StoryContext ctx, StoryEventData data) {
            if (data.TargetTile == null || !data.TargetTile.IsActive) {
                data.FailEvent(ctx.S, "Must have active shuttle set on TargetTile!");
                return;
            }
            if (!data.TargetTile.TryGetComponent<ShuttleComp>(out var shuttle)) {
                data.FailEvent(ctx.S, "TargetTile not a shuttle!");
                return;
            }
            if (shuttle.IsOut) { return; }

            CmdSpawnBeing cmd = null;
            int crawlerCount = Mathf.CeilToInt(data.SeededRng.Range(4, 8) * ctx.S.Difficulty.Level);
            if (crawlerCount > ctx.S.Situation.Friendlies / 2) {
                crawlerCount = ctx.S.Situation.Friendlies / 2;
            }

            if (crawlerCount < 2) {
                crawlerCount = 2;
            }

            var crawler = Species.Get(SpeciesIdH.Crawler);

            for (int i = 0; i < crawlerCount; i++) {
                cmd = new CmdSpawnBeing(new GenBeingVars(crawler, BeingType.Neutral),
                    shuttle.Tile.Transform.WorkSpotOrPos);
                cmd.Execute(ctx.S);
            }
            var being = cmd.SpawnedBeing;
            if (being == null) {
                data.FailEvent(ctx.S,
                    $"Failed spawning crawler at shuttle position: {shuttle.Entity.Position}");
                return;
            }
            data.TargetBeing = being.Data;
            The.AchievementTracker?.Unlock(AchievementCrawlerUber.AchievementId);
            data.CreateLogEntry(IconId.CPreviewCrawler01, T.EvtShuttleCrawlersTitle,
                    T.EvtShuttleCrawlersDesc_V2.F(crawlerCount,
                        being.Definition.CodexLink));
            data.ShowPopup();
            var title = Texts.Red(T.EvtShuttleCrawlersTitle);
            ctx.S.Sys.Log.AddLine(shuttle.Tile, title);
            ctx.S.Sig.ShowFanfare.Send(Texts.LinkToTile(shuttle.Tile, title));
            data.SetState(ctx.S, StoryEventState.Ending);
        }
    }
}
