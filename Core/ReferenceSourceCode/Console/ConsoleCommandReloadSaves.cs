using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using Game.Utils;
using UnityEngine;
using Game.UI;

namespace Game.Console {
    public class ConsoleCommandReloadSaves : ConsoleCommand {
        [SelfInit]

        static void Load() {
            Register(new ConsoleCommandReloadSaves());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            SaveLoadUtils.ClearCache();
            SaveLoadUtils.MigrateOrphanSaves();
            SaveLoadUtils.LoadSavesByGame();
            return OK("Saves reloaded");
        }

        public override void Initialize() {
            Name = "reload_saves";
            HelpLine = "Reloads the game save files from disk";
        }
    }
}
