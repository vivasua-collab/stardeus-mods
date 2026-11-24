#if !ENABLE_IL2CPP
using KL.Console;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandScript : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandScript());
        }

        private ScriptOptions options;

        protected override Dictionary<string, string> subCommands => new() {
            { "run", "Run" },
        };

        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            // Running arbitrary scripts should block achievements
        };

        private ConsoleCommandResult Run(ConsoleCommandArguments args) {
            // Get the full command after "script run "
            var fullCommand = args.FullInput;
            var prefix = "script run ";

            // Find case-insensitive match
            var startIndex = fullCommand.IndexOf(prefix, System.StringComparison.OrdinalIgnoreCase);
            if (startIndex == -1) {
                return Error("No script provided. Usage: script run <code>");
            }

            var code = fullCommand.Substring(startIndex + prefix.Length);
            if (string.IsNullOrWhiteSpace(code)) {
                return Error("No script provided. Usage: script run <code>");
            }

            try {
                // Run async script evaluation
                var task = RunScriptAsync(code);
                task.Wait(); // Wait for completion

                if (task.Result != null) {
                    return OK($"Script executed. Result: {task.Result}");
                } else {
                    return OK("Script executed successfully.");
                }
            } catch (System.Exception ex) {
                return Error($"Script error: {ex.Message}");
            }
        }

        private async Task<object> RunScriptAsync(string code) {
            options ??= ScriptOptions.Default
                .AddReferences(typeof(The).Assembly)
                .AddReferences(typeof(KL.Utils.D).Assembly)
                .AddReferences(typeof(GameObject).Assembly)
                .AddImports("UnityEngine")
                .AddImports("Game")
                .AddImports("Game.Data")
                .AddImports("Game.Systems")
                .AddImports("System")
                .AddImports("KL.Utils")
                .AddImports("System.Linq");

            var globals = new ScriptGlobals {
                S = The.R?.State,
            };

            var result = await CSharpScript.EvaluateAsync(code, options, globals);
            return result;
        }

        public class ScriptGlobals {
            public object The { get; set; }
            public object S { get; set; }
        }

        public override void Initialize() {
            Name = "script";
            HelpLine = "Runs C# script at runtime. Usage: script run <code>";
        }
    }
}
#endif
