using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Space;
using Game.Story.Events;
using Game.Systems.Story;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Space {
    public sealed class SpaceEffectRandomEvent : SpaceEffect {
        [SelfInit]
        private static void Init() {
            Register(new SpaceEffectRandomEvent());
        }

        public override string Id => "RandomEvent";
        public override string Name => null;
        protected override void Cleanup() { }
        public override void OnEnterSector(GameState s, SpaceSector sector) {

            var ctx = s.Query.GetStoryContext.Ask();
            if (ctx.CurrentSector != sector) {
                D.Err("Current Sector != context sector: {0} != {1}",
                    sector, ctx.CurrentSector);
                ctx.CurrentSector = sector;
            }

            sector.SO.SetType(SpaceObjectType.Find(SpaceObjectTypeIdH.AsteroidZone));
            var seed = Hashes.Quick(sector.Seed, Primes.Prime032);
            // hook can't be instant here or it will fail due to us
            // still being in hyperspace
            var hook = StoryEventHook.Create(StoryEventFlags.Space)
                // guarantee some event
                .WithIgnore(StoryPickIgnore.All)
                .Optional()
                .WithSeed(seed);
            hook.BoundToSO = sector.SO.Id;
            s.Query.AddStoryEventHook.Ask(hook);
        }

        public override void OnExitSector(GameState s, SpaceSector sector) {
            sector.Biome.RemoveEffect(Id);
        }

        public override void OnLoad(GameState s, SpaceSector sector) {
        }

        public override void Tick(GameState s, long ticks, SpaceSector currentSector) {
        }
    }
}
