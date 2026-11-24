using Game.Components;
using Game.Data;
using KL.Randomness;
using UnityEngine;
using Game.Systems.Creatures;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class StasisPodDeadTravelerStoryEvent : StoryEvent {
        public const string EventId = "StasisPodDeadTraveler";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new StasisPodDeadTravelerStoryEvent());
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (!StasisPodEventUtils.ValidateEventData(s, data, out StasisPodComp pod)) {
                return;
            }
            data.SetState(s, StoryEventState.Ending);
            var rng = new Rng(pod.ContentsSeed);
            var spawnPos = EntityUtils.SafeSpawnPosFor(pod.Tile, rng.Fork());
            int numBeings = rng.Range(1, 3);
            var chanceInfected = s.Difficulty.Level * 0.5f;
            if (chanceInfected > 0.75f) {
                chanceInfected = 0.75f;
            }
            var chanceWeaponized = 0.5f;
            BeingUtils.GenerateDeadBeings(s, numBeings, rng, spawnPos,
                chanceInfected, chanceWeaponized);
            pod.Open();
            data.HideLogEntry();
        }
    }
}
