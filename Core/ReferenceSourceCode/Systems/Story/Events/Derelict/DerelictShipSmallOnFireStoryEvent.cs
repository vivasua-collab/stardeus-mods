using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipSmallOnFireStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipSmallOnFire";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipSmallOnFireStoryEvent());
        }
        protected override float chanceOfSmallShip => 1f;

        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new DestroyShipGenStage());
            factory.AddStage(new FireGenStage(0.5f));
        }
    }
}
