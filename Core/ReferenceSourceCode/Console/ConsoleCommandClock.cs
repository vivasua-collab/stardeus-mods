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
    public class ConsoleCommandClock : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandClock());
        }

        protected override Dictionary<string, string> subCommands => new() {
            {"set_days", "SetDays"}
        };


        protected ConsoleCommandResult SetDays(ConsoleCommandArguments args) {
            if (!args.HasArgument(2)) {
                return Error("days not given");
            }
            var days = args.GetFloat(2);
            A.S.Clock.Load((int) (Consts.TicksPerDay * days));
            return OK("Set days to {0}", days);
        }

        public override void Initialize() {
            Name = "clock";
            HelpLine = "Clock manipulation";
        }
    }
}
