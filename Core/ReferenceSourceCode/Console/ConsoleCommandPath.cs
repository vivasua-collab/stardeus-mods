using KL.Console;
using KL.Utils;
using Game.Constants;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using Game.UI;
using Game.Systems.Path;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandPath : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandPath());
        }
        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "debug",
            "status",
            "resetstats"
        };

        protected override Dictionary<string, string> subCommands => new() {
            {"debug", "ShowDebug"},
            {"status", "Status"},
            {"resetstats", "ResetStats"},
        };

        private static PathDebugOutput currentDebugOutput;

        protected ConsoleCommandResult ShowDebug(ConsoleCommandArguments args) {
            var pathSys = The.R.Sys.Path;
            currentDebugOutput = new PathDebugOutput(pathSys);
            DebugOutputWidget.SpawnFor(currentDebugOutput);
            return Close();
        }

        protected ConsoleCommandResult ResetStats(ConsoleCommandArguments args) {
            if (currentDebugOutput != null) {
                currentDebugOutput.ResetStatistics();
                return OK("Path debug statistics reset.");
            } else {
                return OK("No active debug output found. Use 'path debug' first.");
            }
        }

        protected ConsoleCommandResult Status(ConsoleCommandArguments args) {
            var pathSys = The.R.Sys.Path;
            var sb = Caches.StringBuilder;
            sb.Clear();

            sb.AppendFormat("Teleporters: {0}", pathSys.TeleporterCount);
            sb.AppendFormat("\nClaimed Positions: {0}", pathSys.ClaimedPosBeingToPosMap.Count);
            sb.AppendFormat("\nHas Many Teleporters: {0}", pathSys.HasManyTeleporters);
            sb.AppendFormat("\nHas Unreachable Areas: {0}", pathSys.HasUnreachableAreas);

            return OK(sb.ToString());
        }

        public override void Initialize() {
            Name = "Path";
            HelpLine = "Pathfinding debugging";
        }
    }
}
