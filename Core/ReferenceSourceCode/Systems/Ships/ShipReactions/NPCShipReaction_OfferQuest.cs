using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Diplomacy;
using Game.Utils;
using UnityEngine;

namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_OfferQuest : NPCShipReaction {
        public const string ReactionId = "OfferQuest";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_OfferQuest());
        }

        public override ShipReactionType Type => ShipReactionType.Positive;
        public override string Id => ReactionId;
        public override int N2PCooldownTicks => Consts.TicksPer3Months;

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdj) {

            chanceAdj = 0f;
            if (!observer.IsQuestGiver) { return false; }
            if (!ship.IsPlayer) { return false; }
            if (A.S.Query.IsActiveQuestProvider.Ask(observer)) {
                return false;
            }

            chanceAdj = 0f;
            return true;
        }

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            return NPCShipBehavior_Sector_OfferQuest.AssignTo(observer, ship.SO);
        }
    }
}
