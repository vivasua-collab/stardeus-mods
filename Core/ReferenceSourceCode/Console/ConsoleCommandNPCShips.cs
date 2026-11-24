using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using KL.Randomness;
using KL.Collections;
using Game.Procgen.Space;
using Game.Data.Space;
using Game.Input;
using System.Text;
using Game.CodeGen;
using Game.Systems.Space;
using Game.Systems.Ships;
using Game.Systems;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandNPCShips : BaseConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandNPCShips());
        }
        protected override Dictionary<string, string> subCommands => new() {
            { "spawn_ship", "SpawnShip" },
            { "do_turn", "NPCShipTurn" },
            { "populate_universe", "PopulateUniverse" },
            { "depopulate_universe", "DepopulateUniverse" },
            { "ship_behavior_region", "SetShipBehaviorRegion" },
            { "ship_react", "SetShipReaction" },
            { "jump_to_player", "JumpToPlayer" },
            { "enter_hyperspace", "EnterHyperspace" },
            { "jump_from_player", "JumpFromPlayer" },
            { "turn_into_derelict", "TurnIntoDerelict" },
            { "notice_player", "NoticePlayer" },
            { "show_info", "ShowInfo" },
            { "list", "ListShips" },
        };
        private ConsoleCommandResult ListShips(ConsoleCommandArguments args) {
            var ships = A.S.Query.NPCShipsAt.Ask(A.PlayerSector.Sector);
            if (Arrays.IsEmpty(ships)) {
                return Error("No ships in sector {0}", A.PlayerSector);
            }
            var sb = Caches.StringBuilder;
            sb.Clear();
            foreach (var ship in ships) {
                sb.AppendFormat("- {0}\n", ship.ToString());
            }
            return OK(sb.ToString());
        }

        private ConsoleCommandResult NoticePlayer(ConsoleCommandArguments args) {
            var sys = The.R.Sys.NPCShip;
            var s = The.R.State;
            var player = s.Universe.Player;
            sys.OnNPCShipEnterSector(player.Ship, player.Ship.Sector, true);
            return OK("Noticing player");
        }
        private ConsoleCommandResult TurnIntoDerelict(ConsoleCommandArguments args) {
            var sys = The.R.Sys.NPCShip;
            var s = The.R.State;
            var player = s.Universe.Player;
            var ship = sys.Ships.FirstOrDefault(
                s => !s.IsInHyperspace && !s.IsPlayer && !s.IsDerelict && s.Sector == player.Ship.Sector);
            if (ship == null) {
                return Error("Couldn't find a ship in the same sector as player");
            }
            ship.TurnIntoDerelict(null);
            return OK("Ship turned into derelict: {0}", ship);
        }
        private ConsoleCommandResult JumpFromPlayer(ConsoleCommandArguments args) {
            var sys = The.R.Sys.NPCShip;
            var s = The.R.State;
            var player = s.Universe.Player;
            var ship = sys.Ships.FirstOrDefault(
                s => s.IsInHyperspace && !s.IsPlayer && s.Sector == player.Ship.Sector);
            if (ship == null) {
                return Error("Couldn't find a ship in the same sector as player");
            }
            var dest = ship.Sector.Links.First().OtherSideOf(ship.Sector);
            ship.HyperjumpTo((SpaceSector) dest);
            return OK("Ship jumped: {0}->{1}", ship, dest);
        }
        private ConsoleCommandResult EnterHyperspace(ConsoleCommandArguments args) {
            var sys = The.R.Sys.NPCShip;
            var s = The.R.State;
            var player = s.Universe.Player;
            var ship = sys.Ships.FirstOrDefault(
                s => !s.IsInHyperspace && !s.IsPlayer && s.Sector == player.Ship.Sector);
            if (ship == null) {
                return Error("Couldn't find a ship in the same sector as player");
            }
            ship.EnterHyperspace();
            return OK("Ship entered hyperspace: {0}", ship);
        }
        private ConsoleCommandResult JumpToPlayer(ConsoleCommandArguments args) {
            var sys = The.R.Sys.NPCShip;
            var s = The.R.State;
            var player = s.Universe.Player;
            var ship = sys.Ships.FirstOrDefault(
                s => s.Sector.Region == player.Ship.Sector.Region
                    && s.Sector != player.Ship.Sector);
            if (ship == null) {
                return Error("Couldn't find a ship in the same region not in the same sector");
            }
            if (!ship.IsInHyperspace) {
                ship.EnterHyperspace();
            }
            ship.HyperjumpTo(player.Ship.Sector);
            if (args.HasArgument(2)) {
                var dist = args.GetFloat(2);
                var loc = player.Location + Rng.UInsideUnitCircle().normalized * dist;
                D.Warn("Loc / dist {0} / {1}", loc, dist);
                ship.ExitHyperspace(loc);
            } else {
                ship.ExitHyperspace();
            }
            return OK("Ship jumped to player sector: {0}", ship);
        }
        private ConsoleCommandResult ShowInfo(ConsoleCommandArguments args) {
            var shipId = args.GetUInt(2);
            var shipSys = The.R.Sys.NPCShip;
            var ship = shipSys.Ships.FirstOrDefault(ship => ship.Id == shipId);
            if (ship == null) {
                return Error("NPCShip not found by id: {0}", shipId);
            }
            return OK(ship.DebugInfo);
        }

        private ConsoleCommandResult SetShipReaction(ConsoleCommandArguments args) {
            if (!args.HasArgument(2)) {
                var ships = A.S.Query.NPCShipsAt.Ask(A.PlayerSector.Sector);
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendFormat("{0} ships will react to player:\n", ships.Count - 1);
                foreach (var sh in ships) {
                    if (sh.IsPlayer) { continue; }

                    sh.Notice(A.Player.Ship, 0f);
                    sb.AppendFormat(" - {0} => {1}\n", sh.NameFull,
                        sh.BehaviorSector?.Id);
                }
                return OK(sb.ToString());
            }
            var shipId = args.GetUInt(2);
            var shipId2 = args.GetUInt(3);
            var reactionId = args.GetString(4);
            var shipSys = The.R.Sys.NPCShip;
            var ship = shipSys.Ships.FirstOrDefault(ship => ship.Id == shipId);
            if (ship == null) {
                return Error("NPCShip not found by id: {0}", shipId);
            }
            var ship2 = shipSys.Ships.FirstOrDefault(ship => ship.Id == shipId2);
            if (ship2 == null) {
                return Error("NPCShip not found by id: {0}", shipId2);
            }
            var re = Registries.NPCShipReactions.Get(reactionId);
            if (re == null) {
                return Error("NPCShipReaction not found by id: {0}", reactionId);
            }
            re.ReactTo(ship, ship2);
            return OK("Executed reaction {0} on ships: {1} -> {2}", re, ship, ship2);
        }
        private ConsoleCommandResult SetShipBehaviorRegion(ConsoleCommandArguments args) {
            var shipId = args.GetUInt(2);
            var shipSys = The.R.Sys.NPCShip;
            var ship = shipSys.Ships.FirstOrDefault(ship => ship.Id == shipId);
            if (ship == null) {
                return Error("NPCShip not found by id: {0}", shipId);
            }
            var behId = args.GetString(3);
            var beh = Registries.NPCShipBehaviors.Get(behId);
            if (beh == null) {
                return Error("NPCShipBehavior not found by id: {0}", behId);
            }
            ship.SetBehavior(beh, NPCShipBehaviorScope.Region);

            return OK("Set behavior on ship: {0} -> {1}", ship, beh);
        }

        private ConsoleCommandResult SpawnShip(ConsoleCommandArguments args) {
            var s = The.R.State;
            var ship = s.Query.SpawnShipAt.Ask(s.Universe.CurrentSector);
            ship.TeleportToSectorLocation(s.Universe.Player.Location + Rng.UInsideUnitCircle() * 2f);
            return OK("Spawned ship: {0}", ship);
        }
        private ConsoleCommandResult NPCShipTurn(ConsoleCommandArguments args) {
            var s = The.R.State;
            var shipSys = The.R.Sys.NPCShip;
            shipSys.DoTurnRegion();
            return OK("Did a NPC ship turn");
        }

        private ConsoleCommandResult PopulateUniverse(ConsoleCommandArguments args) {
            var shipSys = The.R.Sys.NPCShip;
            shipSys.PopulateUniverse(The.R.State.Universe);
            return OK("Populated universe with NPC ships");
        }
        private ConsoleCommandResult DepopulateUniverse(ConsoleCommandArguments args) {
            var shipSys = The.R.Sys.NPCShip;
            shipSys.DepopulateUniverse(keepPlayer: true);
            return OK("Depopulated universe with NPC ships");
        }

        public override void Initialize() {
            Name = "npc_ships";
            HelpLine = "Tools for NPC Ships";
        }
    }
}
