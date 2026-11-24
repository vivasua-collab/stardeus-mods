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
using Game.Systems.Story;
using Game.Systems.Notify;

namespace Game.Systems.Story.Events {
    public sealed class EggHatchCrawlerGooStoryEvent : EggHatchBaseStoryEvent {
        public const string EventId = "EggHatchCrawlerGoo";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new EggHatchCrawlerGooStoryEvent());
        }

        protected override void DoSpawn(StoryContext ctx, StoryEventData data, EggComp egg, Vector2 eggPos) {
            var rng = data.SeededRng;
            var gooMat = MatType.FastGet(MatIdH.CrawlerGoo);
            var amt = rng.Range(3, 6);
            var pos = EntityUtils.ToPos(eggPos);
            var spawned = EntityUtils.SpawnRawMaterial(gooMat, amt, pos, 0.5f);
            if (spawned == null) {
                return;
            }
            data.CreateLogEntry(GraphicsId.ObjMaterialsCrawlerGoo, T.EvtEggHatchedCrawlersTitle, T.ObjMaterialsCrawlergooDesc);
            // No EntityId on StoryLogEntry in new system, so skip that assignment
            Notification.BuildFor(spawned.Obj, null, data.LogEntry.Title,
                    Consts.TicksPer3Days, NotificationType.Info)
                .WithOnClick(data.ShowPopup)
                .Show();
        }

    }
}
