using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.Diplomacy;
using Game.Systems.Ships;
using Game.Systems.Space;
using Game.Utils;
using KL.Randomness;
using UnityEngine;

namespace Game.Systems.Story {
    public sealed class PirateShipExtortStoryEvent : StoryEvent {
        public const string EventId = "PirateShipExtort";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new PirateShipExtortStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            return !A.IsInHyperspace && !A.Player.Ship.IsDerelict
                && !SpaceEffectMagneticStorm.IsOngoing(ctx.S);
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            if (A.Player.Ship.IsDerelict) {
                data.FailEvent(s, "Player is derelict", isHardFailure: false);
                return;
            }
            var parent = A.Player.Parent;
            var tpl = new NPCShip();
            tpl.Faction = FactionDef.FastGet(FactionIdH.Pirate);
            var ship = s.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, tpl);
            var loc = SOUtils.RandomLocationInside(parent, Rng.Unseeded);
            ship.TeleportToSectorLocation(loc);
            data.TargetSO = ship.SO;
            NPCShipBehavior_Sector_ExchangeGift.AssignTo(ship, s.Universe.Player,
                GiftDirection.NPCToPlayer, GiftMode.Extortion);
            data.HideLogEntry();
            data.SetState(ctx.S, StoryEventState.Ending);
        }
    }
}
