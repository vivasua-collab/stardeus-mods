using Game.Data;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_TradeWith : NPCShipReaction {
        public const string ReactionId = "TradeWith";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_TradeWith());
        }
        public override string Id => ReactionId;
        public override ShipReactionType Type => ShipReactionType.Positive;

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdj) {
            chanceAdj = 0f;
            if (observer.IsQuestGiver && ship.IsPlayer
                    && A.S.Query.IsActiveQuestProvider.Ask(observer)) {
                return false;
            }
            return observer.WillTrade && (ship.WillTrade || ship.IsPlayer);
        }

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            return NPCShipBehavior_Sector_TradeWith.AssignTo(observer, ship.SO, false);
        }
    }
}
