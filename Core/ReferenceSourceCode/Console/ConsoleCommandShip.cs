using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using System.Text;
using Game.Data.Economy;
using KL.Randomness;
using System.Linq;
using Game.Systems;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandShip : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandShip());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "rotate_cw", "RotateCW" },
            { "rotate_ccw", "RotateCCW" },
            { "rotate_180", "Rotate180" },
            { "recenter", "Recenter" },
        };

        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "rotate_cw",
            "rotate_ccw",
            "rotate_180",
            "recenter"
        };

        private ConsoleCommandResult RotateCW(ConsoleCommandArguments args) {
            Rotate(1);
            return OK("Rotating CW");
        }
        private ConsoleCommandResult RotateCCW(ConsoleCommandArguments args) {
            Rotate(-1);
            return OK("Rotating CCW");
        }
        private ConsoleCommandResult Rotate180(ConsoleCommandArguments args) {
            Rotate(1);
            Rotate(1);
            return OK("Rotating 180");
        }

        private ConsoleCommandResult Recenter(ConsoleCommandArguments args) {
            var s = The.R.State;
            if (!CmdRecenterShip.TryFindShipCenter(s, out var center)) {
                return Error("No ship to recenter: no ship computer on any section");
            }
            s.CmdQ.Enqueue(new CmdRecenterShip(center));
            return OK("Recentering ship");
        }

        private void Rotate(sbyte dir) {
            The.R.State.CmdQ.Enqueue(new CmdRotateSection(dir));
        }

        public override void Initialize() {
            Name = "ship";
            HelpLine = "Manipulates the ship";
        }
    }
}
