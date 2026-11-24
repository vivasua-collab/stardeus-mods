using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipSmallRektStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipSmallRekt";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new DerelictShipSmallRektStoryEvent());
        }
        protected override float chanceOfSmallShip => 1f;
        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
            factory.AddStage(new DestroyShipGenStage());
        }
    }
}
