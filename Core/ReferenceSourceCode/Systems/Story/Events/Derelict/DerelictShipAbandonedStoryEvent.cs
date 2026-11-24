using Game.Procgen;
using KL.Randomness;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class DerelictShipAbandonedStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "DerelictShipAbandoned";
        public override string Id => EventId;

        [SelfInit]
        private static void Register() {
            Register(new DerelictShipAbandonedStoryEvent());
        }
        protected override void AddDestructionStages(ShipFactory factory, Rng rng) {
        }
    }
}
