using System;
using Game.Achievements;
using Game.Data;
using KL.Console;
using KL.Utils;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandTunable : ConsoleCommand {
        [SelfInit]
        static void Load() {
            Register(new ConsoleCommandTunable());
        }

        public override string Autocomplete(ConsoleCommandArguments args) {
            if (!args.HasArgument(1)) { return null; }
            if (args.GetString(1).StartsWith("r")) {
                return "reload";
            }
            var getset = "set";
            if (args.GetString(1).StartsWith("g")) { getset = "get"; }
            if (!args.HasArgument(2)) {
                return getset;
            }
            var type = args.GetString(2);
            if (!args.HasArgument(3)) {
                if (type.StartsWith("s")) { return $"{getset} string"; }
                else if (type.StartsWith("i")) { return $"{getset} int"; }
                else if (type.StartsWith("b")) { return $"{getset} bool"; }
                return $"{getset} float";
            }
            var key = args.GetString(3);
            return string.Format("{0} {1} {2}", getset, type,
                Strings.Autocomplete(key, Tunable.AllKeys));
        }

        #pragma warning disable 0618
        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var action = args.GetString(1);
            if (action == "reload") {
                The.ModLoader.ReloadTunables();
                return OK("Reloading tunables. WARNING: Many tunable values are cached in components, and may not get refreshed without restart.");
            }
            if (!args.HasArgument(3)) {
                return OK("Available settings: {0}",
                    string.Join("\n", Tunable.AllKeys));
            }
            var type = args.GetString(2);
            var key = args.GetString(3);
            if (action == "get") {
                switch (type) {
                    case "bool": return OK(Tunable.Bool(key).ToString());
                    case "int": return OK(Tunable.Int(key).ToString());
                    case "float": return OK(Tunable.Float(key).ToString());
                    case "string": return OK(Tunable.String(key));
                }
                return Error("Setting not available: {0}", key);
            }
            if (action == "set") {
                AchievementTracker.BlockGlobally(
                    "Changing any tunable blocks achievements until the game is completely restarted (exit to OS + rerun).\n\nLoading any game save right now will disable achievements for that save.");
                if (The.R?.State.IsLoaded == true) {
                    The.R.State.IsAchievementsBlocked = true;
                }
                if (!args.HasArgument(3)) {
                    return Error("Usage: /setting [get|set] <type> <key> [value]");
                }
                switch (type) {
                    case "bool": {
                        var data = args.GetBool(4, out var validBool);
                        if (validBool) {
                            var old = Tunable.Bool(key);
                            Tunable.SetBool(key, data);
                            return OK("{0}: {1} -> {2}", key, old, data);
                        } else {
                            return Error("{0} is not a valid bool", args.GetString(3));
                        }
                    }
                    case "float": {
                        var data = args.GetFloat(4);
                        var old = Tunable.Float(key);
                        Tunable.SetFloat(key, data);
                        return OK("{0}: {1} -> {2}", key, old, data);
                    }
                    case "int": {
                        var data = args.GetInt(4);
                        var old = Tunable.Int(key);
                        Tunable.SetInt(key, data);
                        return OK("{0}: {1} -> {2}", key, old, data);
                    }
                    case "string": {
                        var data = args.GetString(4);
                        var old = Tunable.String(key);
                        Tunable.SetString(key, data);
                        return OK("{0}: {1} -> {2}", key, old, data);
                    }
                }
                return Error("Setting not available: {0}", key);
            }
            return Error("Usage: /setting [get|set] <type> <key> [value]");
        }
        #pragma warning restore 0618

        public override void Initialize() {
            Name = "tunable";
            HelpLine = "Manipulate tunable variables on the fly";
        }
    }
}
