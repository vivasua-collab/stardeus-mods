using Game.Constants;
using Game.Data;
using Game.Systems.Diplomacy;
using Game.Systems.Space;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_Extort : NPCShipReaction {
        public const string ReactionId = "Extort";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_Extort());
        }
        public override string Id => ReactionId;
        public override ShipReactionType Type => ShipReactionType.Negative;

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdj) {

            if (ship.Faction == observer.Faction) {
                chanceAdj = 0f;
                return false;
            }

            // Magnetic storm: peaceful sector - no extortion against the player
            if (ship.IsPlayer && SpaceEffectMagneticStorm.IsOngoing(A.S)) {
                chanceAdj = 0f;
                return false;
            }

            if (observer.IsQuestGiver && ship.IsPlayer
                    && A.S.Query.IsActiveQuestProvider.Ask(observer)) {
                chanceAdj = 0f;
                return false;
            }

            if (observer.IsPolice && !ship.IsOutlaw) {
                if (observer.SeededRng(Primes.Prime009).Chance(0.95f)) {
                    chanceAdj = 0f;
                    return false;
                }
                // otherwise a corrupt cop
            }

            chanceAdj = 0f;
            var powerDiff = observer.StrengthDifferenceAgainst(ship);
            var extortChance = observer.Faction.BraveryCurve.Evaluate(powerDiff);

            if (extortChance <= 0f) { return false; }

            if (observer.Memory.HasCustom) {
                if (observer.Memory.TryCustom(
                        NPCShipBehavior_Sector_ExchangeGift.Key_Long_ExtortionAvailableAfter,
                            out long availableAfter)) {
                    return G.Ticks > availableAfter;
                }
            }
            return false;
        }

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            return NPCShipBehavior_Sector_ExchangeGift.AssignTo(observer, ship.SO,
                ship.IsPlayer ? GiftDirection.NPCToPlayer : GiftDirection.NPCToNPC,
                GiftMode.Extortion);
        }
    }
}
