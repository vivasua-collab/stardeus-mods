using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipWithSurvivorZoneStoryEvent : BaseDerelictShipStoryEvent {
    public const string EventId = "DerelictShipWithSurvivorZone";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipWithSurvivorZoneStoryEvent());
        }

        protected override void AddPostStructureStages(ShipFactory factory, Rng rng) {
            var genStage = new SurvivorAreaGenStage(5, 20, true);
            genStage.GreetColonists = true;
            // Survivors keep the derelict's own faction - guests when it is friendly,
            // hostile faction members otherwise (see SurvivorAreaGenStage).
            genStage.SurvivorFaction = factory.ForShip?.Faction;
            factory.AddStage(genStage);
        }
    }
}
