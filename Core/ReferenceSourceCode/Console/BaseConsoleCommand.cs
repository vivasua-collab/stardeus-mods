using KL.Console;
using KL.Utils;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using Game.Constants;

namespace Game.Console {
    public abstract class BaseConsoleCommand : ConsoleCommand {
        protected abstract Dictionary<string, string> subCommands { get; }
        protected virtual HashSet<string> commandsThatDontBlockAchievements => null;

        public override string Autocomplete(ConsoleCommandArguments args) {
            var key = args.GetString(1);
            return Strings.Autocomplete(key, subCommands.Keys);
        }

        // TODO refactor with ConsoleCommandCombat
        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            if (!args.HasArgument(1)) {
                return OK("Subcommads: {0}", string.Join(",", subCommands.Keys));
            }
            var subcmd = args.GetString(1);
            var cmdFunc = subCommands.Get(subcmd, null);
            if (cmdFunc == null) {
                return Error("Unknown subcommand: {0}. Subcommands: {1}",
                    subcmd, string.Join(",", subCommands.Keys));
            }
            var meth = GetType().GetMethod(cmdFunc, BindingFlags.Instance | BindingFlags.NonPublic);
            if (meth == null) {
                return Error("Method not found: {0}", cmdFunc);
            }
            if (A.HasRuntime && !The.R.State.IsAchievementsBlocked) {
                if (commandsThatDontBlockAchievements?.Contains(subcmd) != true) {
                    The.R.State.IsAchievementsBlocked = true;
                    D.Err("Blocking achievements due to execution of command: {0}", args.FullInput);
                }
            }
            try {
                return (ConsoleCommandResult) meth.Invoke(this, new object[] { args });
            } catch (Exception ex) {
                D.LogEx(ex, "Command failure: {0}", args.FullInput);
                return Error("Error: Command failed, possibly due to invalid number of parameters: {0} | {1}",
                    ex.Message, ex.StackTrace);
            }
        }
    }
}
