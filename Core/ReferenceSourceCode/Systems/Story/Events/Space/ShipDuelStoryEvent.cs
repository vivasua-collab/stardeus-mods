using System;
using Game.Commands;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Data.Space;
using Game.Systems.AI.Combat;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using Game.CodeGen;
using KL.Clock;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;
using Game.Audio;

namespace Game.Systems.Story {
    public sealed class ShipDuelStoryEvent : StoryEvent {
        public const string EventId = "ShipDuel";
        public override string Id => EventId;
        [SelfInit]
        private static void Register() {
            Register(new ShipDuelStoryEvent());
        }

        public override bool CheckConditions(StoryContext ctx, StoryEventDef def) {
            if (A.IsInHyperspace) {
                return false;
            }
            var npcShips = ctx.S.Query.NPCShipsAt.Ask(ctx.CurrentSector);
            if (npcShips == null) {
                return true;
            }
            if (npcShips.Count > 4) {
                return false;
            }
            return true;
        }

        public override void OnStart(StoryContext ctx, StoryEventData data) {
            var s = ctx.S;
            data.HideLogEntry();
            var attackerTemplate = new NPCShip();
            attackerTemplate.Faction = FactionDef.FastGet(FactionIdH.Pirate);
            var defenderTemplate = new NPCShip();
            defenderTemplate.Faction = FactionDef.FastGet(FactionIdH.Capitalist);
            var attacker = s.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, attackerTemplate);
            var defender = s.Query.SpawnOpponentShipFor.Ask(A.Player.Ship, defenderTemplate);
            NPCShipBehavior_Sector_Combat.AssignTo(attacker, defender);
            NPCShipBehavior_Sector_Combat.AssignTo(defender, attacker);
            data.SetState(ctx.S, StoryEventState.Ending);
        }
    }
}
