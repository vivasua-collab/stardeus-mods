using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Ships;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class ShipAsksFuelStoryEvent : StoryEvent {
        public const string EventId = "ShipAsksFuel";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new ShipAsksFuelStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return !A.Player.Ship.IsDerelict &&
                ctx.S.Universe.Player.Parent.Layer == UniverseLayer.Sector &&
                ctx.S.Sys.Inventory.AvailableCount(MatType.FastGet(MatIdH.FusionCell)) > 2;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (A.Player.Ship.IsDerelict) {
                data.FailEvent(s, "Player is derelict", isHardFailure: false);
                return;
            }
            data.HideLogEntry();
            var parent = A.Player.Parent;
            var ship = s.Query.SpawnShipAt.Ask(parent);
            var loc = SOUtils.RandomLocationInside(parent, Rng.Unseeded);
            ship.TeleportToSectorLocation(loc);

            NPCShipBehavior_Sector_ExchangeGift.AssignTo(ship, s.Universe.Player,
                GiftDirection.NPCToPlayer, GiftMode.Fuel);
            data.SetState(ctx.S, StoryEventState.Ending);
        }
    }
}
