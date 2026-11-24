using Game.Constants;
using Game.Data;
using Game.Systems.Diplomacy;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_GiveGift : NPCShipReaction {
        public const string ReactionId = "GiveGift";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_GiveGift());
        }
        public override string Id => ReactionId;
        public override ShipReactionType Type => ShipReactionType.Positive;

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdj) {
            chanceAdj = 0f;
            if (ship.Faction == observer.Faction) {
                return false;
            }
            if (observer.IsQuestGiver && ship.IsPlayer
                    && A.S.Query.IsActiveQuestProvider.Ask(observer)) {
                return false;
            }
            if (observer.WillTrade && (ship.WillTrade || ship.IsPlayer)) {
                if (observer.Memory.HasCustom) {
                    if (observer.Memory.TryCustom(
                            NPCShipBehavior_Sector_ExchangeGift.Key_Long_NPCGiftAvailableAfter,
                                out long availableAfter)) {
                        return G.Ticks > availableAfter;
                    }
                }
                return true;
            }
            return false;
        }

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            return NPCShipBehavior_Sector_ExchangeGift.AssignTo(observer, ship.SO,
                ship.IsPlayer ? GiftDirection.NPCToPlayer : GiftDirection.NPCToNPC,
                GiftMode.Gift);
        }
    }
}
