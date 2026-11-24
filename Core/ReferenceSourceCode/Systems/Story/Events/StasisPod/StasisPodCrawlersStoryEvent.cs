using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Systems.Creatures;
using Game.Systems.Notify;
using Game.Utils;
using Game.CodeGen;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Systems.Naming;
using Game.Input;

namespace Game.Systems.Story {
    public sealed class StasisPodCrawlersStoryEvent : StoryEvent {
        public const string EventId = "StasisPodCrawlers";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new StasisPodCrawlersStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return A.IsCombatOn;
        }

        private void CreateLogEntry(StoryEventData data, int count) {
            data.CreateLogEntry(IconId.CPreviewCrawler01,
                    $"{T.ObjectsDevicesStasispod} {Texts.Accented(GlyphMap.ArrowRight)} {T.SpeciesCrawler} <b>x{count}</b>",
                    T.EvtStasisPodCrawlersDesc);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);

            var rng = new Rng(pod.ContentsSeed);
            var spawnPos = EntityUtils.SafeSpawnPosFor(pod.Tile, rng.Fork());
            var crawlerExitPoint = EntityUtils.CenterOf(spawnPos);

            var numCrawlers = Mathf.RoundToInt(s.Beings.AliveCount * 0.33f
                * s.Rng.Range(0.9f, 1.1f));
            int minCrawlers = Mathf.RoundToInt(3 * s.Difficulty.Level) + 1;
            int maxCrawlers = Mathf.RoundToInt(10 * s.Difficulty.Level) + 2;
            numCrawlers = Mathf.Clamp(numCrawlers, minCrawlers, maxCrawlers);
            var crawler = Species.Get(SpeciesIdH.Crawler);
            for (int i = 0; i < numCrawlers; i++) {
                var vars = new GenBeingVars(crawler, BeingType.Neutral);
                s.CmdQ.Enqueue(new CmdSpawnBeing(vars, crawlerExitPoint));
            }
            pod.Open();
            CreateLogEntry(data, numCrawlers);
            Notification.BuildFor(pod.Entity, IconId.CPreviewCrawler01, T.EvtStasisPodCrawlersTitle,
                    Consts.TicksPer5Minutes, NotificationType.Critical)
                .WithOnClick(data.ShowPopup)
                .Emergency(true)
                .Show();
            s.Sig.ShowFanfare.Send(Texts.LinkToTile(pod.Tile,
                Texts.Yellow(T.EvtStasisPodCrawlersTitle)));
            s.Sys.Log.AddLine(pod.Tile, T.EvtStasisPodCrawlersTitle);
        }
    }
}
