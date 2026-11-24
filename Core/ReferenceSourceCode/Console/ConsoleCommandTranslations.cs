using System.Collections.Generic;
using Game.Commands;
using Game.Constants;
using KL.Console;
using Game.Utils;
using Game.CodeGen;
using System.Text;
using KL.Collections;
using KL.Utils;
using UnityEngine;

namespace Game.Console {
    public class ConsoleCommandTranslations : ConsoleCommand {
        [SelfInit]
        static void Load() {
            Register(new ConsoleCommandTranslations());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var cmd = args.GetString(1);
            var loc = Translations.CurrentLocale;
            if (cmd == "show") {
                if (!args.HasArgument(2)) {
                    return Error("you need to provide a translation key, and optionally some variables to fill in");
                }
                var key = args.GetString(2);
                var vals = new List<string>();
                int numA = 0;
                for (int i = 3; i < 10; i++) {
                    if (args.HasArgument(i)) {
                        numA++;
                        vals.Add(args.GetString(i));
                    }
                }
                return OK(Translations.Preview(key, vals));
            } else if (cmd == "reload") {
                Translations.Reload();
                return OK("Translations reloaded. Some may still be cached in UI!");
            } else if (cmd == "validate") {
                var isValid = TranslationUtils.ValidityReport(
                    out var shortVersion, out var reportDir);
                if (isValid) {
                    return OK($"{loc} is valid!");
                }
                Files.OpenOSFolder(reportDir);
                return OK($"{shortVersion}.\nFull report and TODO csv at {reportDir}");
            } else if (cmd == "integrate") {
                var fileName = args.GetString(2);
                string localeId;
                if (args.HasArgument(3)) {
                    localeId = args.GetString(3);
                } else if (loc == Translations.DefaultLocale && !Translations.CurrentLocaleIsCommunity) {
                    return Error("Current locale is English (the source language) - specify a target locale explicitly, e.g. 'translations integrate Chinese_DONE.csv Chinese'");
                } else {
                    localeId = Translations.CurrentLocaleIsCommunity ? $"{loc}:community" : loc;
                }
                var integrated = TranslationUtils.IntegrateLocale(
                    localeId, fileName, out var integrateSummary, out var integrateDir);
                if (!integrated) {
                    return Error(integrateSummary);
                }
                Files.OpenOSFolder(integrateDir);
                return OK(integrateSummary);
            } else {
                return Error("Valid commands: show, reload, validate, integrate");
            }
        }

        public override string Autocomplete(ConsoleCommandArguments args) {
            if (args.HasArgument(1) && args.GetString(1).StartsWith(
                    "i", System.StringComparison.InvariantCultureIgnoreCase)
                    && args.HasArgument(2)) {
                var dir = Persist.PersistentPath("Translations", Translations.CurrentLocale);
                var files = Files.ListDirectoryFiles(dir, ".csv", false);
                return $"integrate {Strings.Autocomplete(args.GetString(2), files)}";
            }
            if (args.HasArgument(1) && args.GetString(1).StartsWith(
                    "s", System.StringComparison.InvariantCultureIgnoreCase)) {
                return "show";
            }
            if (args.HasArgument(1) && args.GetString(1).StartsWith(
                    "i", System.StringComparison.InvariantCultureIgnoreCase)) {
                return "integrate";
            }
            return "reload";
        }

        public override void Initialize() {
            Name = "translations";
            HelpLine = "Tool for working with translation mods";
        }
    }
}
