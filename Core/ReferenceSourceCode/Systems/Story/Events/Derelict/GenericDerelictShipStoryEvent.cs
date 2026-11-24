using UnityEngine;

using Game.Utils;
namespace Game.Systems.Story {
    public sealed class GenericDerelictShipStoryEvent : BaseDerelictShipStoryEvent {
        public const string EventId = "GenericDerelictShip";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new GenericDerelictShipStoryEvent());
        }
    }
}
