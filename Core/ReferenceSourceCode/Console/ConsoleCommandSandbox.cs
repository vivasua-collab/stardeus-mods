#if !STARDEUS_DEMO
using KL.Utils;
using KL.Console;
using Game.UI;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandSandbox : ConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandSandbox());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            if (The.R?.State.IsLoaded != true) {
                return Error("This command doesn't work in main menu");
            }
            DevMenu.ToggleInstantBuild(!A.InstantBuild);
            return OK($"Sandbox mode set to: {A.InstantBuild}");
        }

        public override void Initialize() {
            Name = "sandbox";
            HelpLine = "Toggles sandbox";
        }
    }
}
#endif
