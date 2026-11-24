using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandLog : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandLog());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var msg = string.Join(' ', args.ArgumentsWithoutCommand);
            if (The.R?.Sys == null) {
                return Error("This command only works in game");
            }
            The.R.Sys.Log.AddLine(msg);
            return OK();
        }

        public override void Initialize() {
            Name = "log";
            HelpLine = "Logs a message";
        }
    }
}
