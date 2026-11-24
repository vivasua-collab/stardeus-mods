using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Diplomacy;
using Game.Systems.Space;
using Game.Systems.Trade;
using KL.Randomness;
using KL.Utils;

namespace Game.Systems.Ships {
    public abstract class ShipHailAction {
        protected static void Register(ShipHailAction a) {
            a.IdH = Hashes.S(a.Id);
            Registries.ShipHailActions.Add(a.Id, a);
        }
        public abstract string Id { get; }
        public int IdH;

        protected bool IsAttackingUs(NPCShip ship) {
            if (ship.BehaviorSector?.IdH == NPCShipBehavior_Sector_Combat.BehaviorIdH) {
                if (ship.Memory.FollowTarget == A.Player) {
                    return true;
                }
            }
            return false;
        }

        protected bool CanFlyToPlayerOrIsNearby(NPCShip npcShip) {
            // it can fly to us
            if (npcShip.NumThrusters > 0) { return true; }
            // no thrusters, nearby only
            npcShip.NearbyPosFrom(A.Player.Location, out var isNear, extraDist: 0.1f);
            return isNear;
        }

        protected bool CanFlyToPlayerOrIsNearby(NPCShip npcShip, ShipHailActionUI ui) {
            // it can fly to us
            if (npcShip.NumThrusters > 0) { return true; }
            // no thrusters, nearby only
            npcShip.NearbyPosFrom(A.Player.Location, out var isNear, extraDist: 0.1f);
            if (!isNear) {
                ui.SetResponse(isLastMsg: false, ShipChatTopic.TooFar,
                    ShipChatResponseType.Negative);
            }
            return isNear;
        }


        protected bool CheckActionPossibility(NPCShip target, ShipHailActionUI ui,
                ShipChatTopic responseTopic, Rng rng, float chance) {

            if (!target.WillTradeWith(A.Player.Ship, out var reason)) {
                if (reason == TradeRefuseReason.Outlaw) {
                    ui.SetResponse(
                        isLastMsg: true,
                        ShipChatTopic.Generic,
                        ShipChatResponseType.Outlaw);
                } else {
                    D.Err("TODO handle reason: {0}", reason);
                    ui.SetResponse(
                        isLastMsg: true,
                        responseTopic, ShipChatResponseType.Negative);
                }
                return false;
            }
            if (rng.Chance(chance)) {
                ui.SetResponse(isLastMsg: false, responseTopic,
                    ShipChatResponseType.Positive);
                return true;
            }

            ui.SetResponse(isLastMsg: true, responseTopic,
                ShipChatResponseType.Negative);
            return false;
        }

        // provide UI button for player -> npc hail + method
        // provide UI "positive" button for npc -> payer hail + method
        // provide UI "negative" button for npc -> payer hail + method

        public override string ToString() {
            return $"NPCShipReaction[{Id}]";
        }

    }
}
