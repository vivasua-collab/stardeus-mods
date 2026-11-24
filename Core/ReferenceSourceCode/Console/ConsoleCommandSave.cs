using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;

using Game.Utils;
namespace Game.Console {
    public class ConsoleCommandSave : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandSave());
        }

        public override string Autocomplete(ConsoleCommandArguments args) {
            if (args.HasArgument(1) && args.GetString(1).StartsWith("o")) {
                return "open_directory";
            }
            return null;
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            var filename = "default";
            if (args.HasArgument(1)) {
                filename = args.GetString(1);
            }

            if (filename == "open_directory") {
                if (!Files.OpenOSFolder(Files.CombinePath(
                        Persist.Root, Consts.SaveDirectory))) {
                    Files.OpenOSFolder(Persist.Root);
                }
                return Close();
            }

            if (The.R.State.IsPermadeath) {
                return Error("To save in No Reload / Permadeath mode, you have to exit the game");
            }

            The.R.CmdQ.Enqueue(new CmdSaveGame(The.R.State, filename, false, false));
            return Close();
        }

        public override void Initialize() {
            Name = "save";
            HelpLine = "Saves the game";
        }
    }
}
