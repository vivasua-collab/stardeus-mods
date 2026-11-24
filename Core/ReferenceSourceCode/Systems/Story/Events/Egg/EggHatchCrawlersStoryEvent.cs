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
    public sealed class EggHatchCrawlersStoryEvent : EggHatchBaseStoryEvent {
        public const string EventId = "EggHatchCrawlers";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new EggHatchCrawlersStoryEvent());
        }

        protected override void DoSpawn(StoryContext ctx, StoryEventData data, EggComp egg, Vector2 eggPos) {
            var wt = 1f; // TODO: get weight from context or data if needed
            var rng = ctx.S.Rng;
            var numCrawlers = Mathf.RoundToInt(wt / 0.5f * rng.Range(0.9f, 1.1f));
            numCrawlers = Mathf.Clamp(numCrawlers, 3, 15);
            var crawler = Species.Get(SpeciesIdH.Crawler);
            Being firstCrawler = null;
            for (int i = 0; i < numCrawlers; i++) {
                var vars = new GenBeingVars(crawler, BeingType.Neutral);
                vars.IsClone = true;
                var beingData = ctx.S.Query.GenBeingFull.Ask(vars, rng);
                var cmd = new CmdSpawnBeing(beingData, eggPos + rng.InsideUnitCircle().normalized * 0.25f);
                cmd.Execute(ctx.S);
                if (firstCrawler == null) {
                    firstCrawler = cmd.SpawnedBeing;
                }
            }
            if (firstCrawler == null) { return; }
            data.CreateLogEntry(IconId.CPreviewCrawler01, T.EvtEggHatchedCrawlersTitle, T.EvtEggHatchedCrawlersDesc);
            var crawlerAlert = ctx.S.Prefs.GetBool(Pref.CrawlerCombatAlert, PrefH.CrawlerCombatAlert, true);
            var title = Texts.LinkToBeing(firstCrawler.Data, Texts.LinkColor(data.LogEntry.Title));
            if (crawlerAlert) {
                Notification.Build(IconId.CPreviewCrawler01,
                    data.LogEntry.Title, Consts.TicksPer8Hours, NotificationType.Alert)
                    .WithOnClick(data.ShowPopup)
                    .Group("crawler_hatch")
                    .Emergency()
                    .Show();
                ctx.S.Sig.ShowFanfare.Send(title);
            }
            ctx.S.Sys.Log.AddLine(title);
        }
    }
}
