using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using KL.Console;
using KL.Utils;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandSettings : ConsoleCommand {
        [SelfInit]
        public static void Load() {
            Register(new ConsoleCommandSettings());
        }

        public override string Autocomplete(ConsoleCommandArguments args) {
            if (!args.HasArgument(1)) { return null; }
            var getset = "set";
            if (args.GetString(1).StartsWith("g")) { getset = "get"; }
            var isInGame = GameState.IsCurrent(The.R?.State);
            if (!isInGame) {
                getset = $"{getset}_global";
            }
            if (!args.HasArgument(2)) {
                return getset;
            }
            var type = args.GetString(2);
            if (type.StartsWith("b")) {
                type = "bool";
            } else if (type.StartsWith("f")) {
                type = "float";
            } else if (type.StartsWith("i")) {
                type = "int";
            } else if (type.StartsWith("s")) {
                type = "string";
            } else {
                type = "bool";
            }
            if (!args.HasArgument(3)) {
                return string.Format("{0} {1}", getset, type);
            }
            var key = args.GetString(3);
            HashSet<string> allNames;
            if (isInGame) {
                allNames = The.R.State.Prefs.AllNames;
            } else {
                allNames = The.Prefs.AllNames;
            }
            return string.Format("{0} {1} {2}", getset, type,
                Strings.Autocomplete(key, allNames));
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            Settings sets;
            var action = args.GetString(1);
            if (action == "get_global" || action == "set_global") {
                sets = The.Prefs;
                action = action.Replace("_global", string.Empty);
            } else if (!GameState.IsCurrent(The.R?.State)) {
                return Error("Only get_global and set_global commands work in the main menu! Load a game to use get / set actions for settings of that game session.");
            } else {
                sets = The.R.State.Prefs;
            }
            if (!args.HasArgument(2)) {
                return OK("Available settings: {0}",
                    string.Join("\n", sets.AllNames));
            }
            if (!args.HasArgument(3)) {
                return Error("Usage: /settings <get|get_global> <bool|float|int|string> <key>");
            }
            var type = args.GetString(2);
            var key = args.GetString(3);
            if (action == "get") {
                switch (type) {
                    case "bool": return OK(sets.GetBool(key, Hashes.S(key), false).ToString());
                    case "float": return OK(sets.GetFloat(key, Hashes.S(key), 0f).ToString());
                    case "int": return OK(sets.GetInt(key, Hashes.S(key), 0).ToString());
                    case "string": return OK(sets.GetString(key, Hashes.S(key), string.Empty).ToString());
                }
                return Error("Setting not available: {0}", key);
            }
            if (action == "set") {
                if (!args.HasArgument(4)) {
                    return Error("Usage: /settings <set|set_global> <bool|float|int|string> <key> <value>");
                }
                if (key == Pref.DevInstantBuild) {
                    The.R.State.IsAchievementsBlocked = true;
                }
                switch (type) {
                    case "bool": {
                        var data = args.GetBool(4, out var validBool);
                        if (validBool) {
                            var old = sets.GetBool(key, Hashes.S(key), false);
                            sets.SetBool(key, data);
                            return OK("{0}: {1} -> {2}", key, old, data);
                        } else {
                            return Error("{0} is not a valid bool", args.GetString(4));
                        }
                    }
                    case "float": {
                        var data = args.GetFloat(4);
                        var old = sets.GetFloat(key, Hashes.S(key), 0f);
                        sets.SetFloat(key, data);
                        return OK("{0}: {1} -> {2}", key, old, data);
                    }
                    case "int": {
                        var data = args.GetInt(4);
                        var old = sets.GetInt(key, Hashes.S(key), 0);
                        sets.SetInt(key, data);
                        return OK("{0}: {1} -> {2}", key, old, data);
                    }
                    case "string": {
                        var data = args.GetString(4);
                        var old = sets.GetString(key, Hashes.S(key), string.Empty);
                        sets.SetString(key, data);
                        return OK("{0}: {1} -> {2}", key, old, data);
                    }
                }
                return Error("Setting not available: {0}", key);
            }
            return Error("Usage: /settings [get|set] <key> [value]");
        }

        public override void Initialize() {
            Name = "settings";
            HelpLine = "Manipulate game settings";
        }
    }
}
