using KL.Utils;
using KL.Console;
using Game.Tools;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandToolbox : ConsoleCommand {
        [SelfInit]
        static void Load() {
            Register(new ConsoleCommandToolbox());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            // Tool list
            if (!args.HasArgument(1)) {
                var vals = The.Toolbox.AvailableToolIds;
                return OK("Available tools:\n{0}", string.Join("\n", vals));
            }
            var toolId = args.GetString(1);
            if (toolId == "clear_recent") {
                The.R.State.RecentItems.Clear();
                The.R.Sig.ResearchChanged.Send(null);
                return OK("Cleared recent blueprints");
            }
            var tool = The.Toolbox.Get(toolId);
            // Tool not found
            if (tool == null) {
                return Error("No tool with id: {0}", toolId);
            }
            The.R.State.IsAchievementsBlocked = true;
            // Non-configurable tool
            if (!tool.IsConfigurable) {
                The.Toolbox.SetActive(tool);
                return OK("Setting tool {0}", toolId);
            }
            // Configured tool
            if (args.HasArgument(2)) {
                var toolArg = args.GetString(2);
                The.Toolbox.SetActive(tool, toolArg);
                return OK("Setting tool {0} with arg {1}", toolId, toolArg);
            } else { // Tool args
                return OK("Available args for tool {0}:\n{1}",
                    toolId, string.Join("\n", tool.AvailableParams));
            }
        }

        public override string Autocomplete(ConsoleCommandArguments args) {
            var arg1 = args.GetString(1);
            if (!args.HasArgument(2)) {
                return Strings.Autocomplete(arg1, The.Toolbox.AvailableToolIds);
            }
            var tool = The.Toolbox.Get(arg1);
            if (tool != null) {
                return string.Format("{0} {1}", arg1,
                    Strings.Autocomplete(args.GetString(2), tool.AvailableParams));
            }
            return null;
        }

        public override void Initialize() {
            Name = "toolbox";
            HelpLine = "Manage tools";
            Args = new Argument[] {
                new() { Name = "tool_id" },
                new() { Name = "tool_command", IsOptional = true },
            };
        }
    }
}
