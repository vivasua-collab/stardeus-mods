using KL.Console;
using Game.Constants;
using Game.Commands;
using Game.Data;
using KL.Randomness;
using Game.UI;
using Game.CodeGen;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandNewGame : ConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandNewGame());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var s = The.R?.State;
            if (s == null) {
                return Error("This command can only be executed while in-game");
            }
            // The.BB.Set(BBKeyH.GlobalSaveType, "template");
            CloneGameUI.ShowTemplatePopup();
            // var ui = new CloneGameUI(s);
            // ui.Show();
            return Close();
        }

        public override void Initialize() {
            Name = "new_game";
            Args = new Argument[] {
                new() { Name = "seed", IsOptional = true },
            };
            HelpLine = T.GameTemplateNew;
        }
    }
}
