using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_Ignore : NPCShipReaction {
        public const string ReactionId = "Ignore";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_Ignore());
        }
        public override string Id => ReactionId;
        public override int N2PCooldownTicks => 0;
        public override ShipReactionType Type => ShipReactionType.Ignore;

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            return false;
        }
    }
}
