using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using Game.Story;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using System.Linq;

namespace Game.Console {
    public sealed class ConsoleCommandDifficulty : ConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandDifficulty());
        }
        public override string Autocomplete(ConsoleCommandArguments args) {
            var arg1 = args.GetString(1);
            return Strings.Autocomplete(arg1,
                Difficulty.AllSorted().Select(s => s.Id));
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var S = The.R.State;
            if (!args.HasArgument(1)) {
                return OK("Current difficulty: {0}. Use 'difficulty <id>' to change it. Options: {1}",
                    S.Difficulty.Id,
                    string.Join(", ", Difficulty.AllSorted().Select(s => s.Id)));
            }
            var id = args.GetString(1);
            foreach (var d in Difficulty.AllSorted()) {
                if (d.Id.ToLowerInvariant() == id.ToLowerInvariant()) {
                    var prevDiff = S.Difficulty.Id;
                    S.Difficulty = d;
                    return OK("Setting difficulty: {0} -> {1}", prevDiff, d.Id);
                }
            }
            return Error("Difficulty not found by id: {0}. Options: {1}", id,
                string.Join(", ", Difficulty.AllSorted().Select(s => s.Id)));
        }

        public override void Initialize() {
            Name = "difficulty";
            HelpLine = "Changes the difficulty";
        }
    }
}
