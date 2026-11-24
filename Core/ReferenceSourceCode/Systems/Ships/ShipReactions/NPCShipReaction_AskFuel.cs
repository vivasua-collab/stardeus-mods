using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Systems.Diplomacy;
using UnityEngine;

using Game.Utils;
namespace Game.Systems.Ships {
    public sealed class NPCShipReaction_AskFuel : NPCShipReaction {
        public const string ReactionId = "AskFuel";
        [SelfInit]
        private static void Register() {
            Register(new NPCShipReaction_AskFuel());
        }

        public override ShipReactionType Type => ShipReactionType.Positive;
        public override string Id => ReactionId;
        public override int N2PCooldownTicks => Consts.TicksPer3Months;
        public override int GlobalN2PCooldownTicks => Consts.TicksPer2Months;

        public override bool IsCompatibleWith(NPCShip observer, NPCShip ship,
                float attitude, out float chanceAdj) {
            chanceAdj = 0f;
            if (observer.IsQuestGiver && ship.IsPlayer
                    && A.S.Query.IsActiveQuestProvider.Ask(observer)) {
                return false;
            }
            if (ship.IsPlayer && ship.SO.S.Sys.Inventory.AvailableCount(
                    MatType.FastGet(MatIdH.FusionCell)) < 3) {
                return false;
            }
            if (observer.Memory.HasCustom) {
                if (observer.Memory.TryCustom(
                        NPCShipBehavior_Sector_ExchangeGift.Key_Long_NPCGiftAvailableAfter,
                            out long availableAfter)) {
                    return G.Ticks > availableAfter;
                }
            }
            return true;
        }

        public override bool ReactTo(NPCShip observer, NPCShip ship) {
            return NPCShipBehavior_Sector_ExchangeGift.AssignTo(observer, ship.SO,
                ship.IsPlayer ? GiftDirection.NPCToPlayer : GiftDirection.NPCToNPC,
                GiftMode.Fuel);
        }
    }
}
