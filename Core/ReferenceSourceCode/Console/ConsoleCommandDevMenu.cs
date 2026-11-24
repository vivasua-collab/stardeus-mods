#if !STARDEUS_DEMO
using KL.Utils;
using KL.Console;
using Game.UI;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandDevMenu : ConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandDevMenu());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            if (The.R?.State.IsLoaded != true) {
                return Error("This command doesn't work in main menu");
            }
            DevMenu.ShowMenu();
            return Close();
        }

        public override void Initialize() {
            Name = "dev_menu";
            HelpLine = "Shows developer tool menu";
        }


    }
}
#endif
