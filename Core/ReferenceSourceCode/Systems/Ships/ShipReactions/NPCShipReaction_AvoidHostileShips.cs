using Game.Constants;
using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_AvoidHostileShips : NPCShipReaction {
        public const string ReactionId = "AvoidHostileShips";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_AvoidHostileShips());
        }
        public override string Id => ReactionId;
        public override int N2PCooldownTicks => 0;
        public override ShipReactionType Type => ShipReactionType.Negative;

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            NPCShipBehavior_Sector_AvoidHostileShips.AssignTo(observer);
            return false;
        }

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float weightAdjustment) {

            if (observer.IsQuestGiver && ship.IsPlayer
                    && A.S.Query.IsActiveQuestProvider.Ask(observer)
                        || observer.NextQuestAvailableAt < G.Ticks) {
                weightAdjustment = 0f;
                return false;
            }

            if (ship.CanBeHostileTowards(observer)) {
                weightAdjustment = observer.FearOf(ship) * 5f;
            } else {
                weightAdjustment = 0f;
            }
            return weightAdjustment > 0f;
        }
    }
}
