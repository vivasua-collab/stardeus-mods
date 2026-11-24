using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_Follow : NPCShipReaction {
        public const string ReactionId = "Follow";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_Follow());
        }
        public override string Id => ReactionId;
        public override ShipReactionType Type => ShipReactionType.Positive;

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdjustment) {
            // We don't want to assign follow behavior to stationary ships
            if (ship.HasLocalTarget || (ship.IsPlayer && ship.SO.S.Sys.Ship.IsMoving)) {
                chanceAdjustment = 1f;
                return true;
            }
            chanceAdjustment = 0f;
            return false;
        }

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            NPCShipBehavior_Sector_Follow.AssignTo(observer, ship.SO);
            return false;
        }
    }
}
