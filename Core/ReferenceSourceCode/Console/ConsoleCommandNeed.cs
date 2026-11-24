using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandNeed : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandNeed());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            #if PRODUCTION_BUILD
            if (!args.HasArgument(1) && !args.HasArgument(2)) {
                return Error("Please provide need name and value");
            }
            #endif
            var need = args.GetString(1);
            var needHash = Hashes.S(need);
            var value = args.GetInt(2);

            foreach (var being in The.R.State.Beings.All) {
                D.Err("FIXME");
                // if (!being.Needs.HasNeed(needHash)) { continue; }
                // var n = being.Needs.GetNeed(needHash);
                // n?.Set(value);
            }
            The.R.State.IsAchievementsBlocked = true;

            return Close();
        }

        public override void Initialize() {
            Name = "need";
            HelpLine = "Sets need value to given one for all beings that have that need";
        }
    }
}
