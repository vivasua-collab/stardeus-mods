using KL.Console;
using KL.Utils;
using Game.Constants;
using Game.Commands;
using KL.Grid;
using UnityEngine;
using Game.Data;
using System.Collections.Generic;
using System;
using KL.Collections;
using System.Reflection;
using System.Text;
using Game.Data.Economy;
using KL.Randomness;
using System.Linq;
using Game.Systems;
using Game.Utils;
using Game.Systems.UIUX;
using Game.UI;
using Game.Visuals.Hull;

namespace Game.Console {
    public class ConsoleCommandTheme : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandTheme());
        }

        protected override Dictionary<string, string> subCommands => new() {
            { "reload", "Reload" },
            { "debug", "ShowDebug" },
        };

        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "reload",
            "debug"
        };
        protected ConsoleCommandResult ShowDebug(ConsoleCommandArguments args) {
            DebugOutputWidget.SpawnFor(new UIThemeDebugOutput());
            return Close();
        }

        private ConsoleCommandResult Reload(ConsoleCommandArguments args) {
            UITheme.ReloadTheme();
            // The hull view styles are looks tuned the same way: edit the JSON, reload, look
            HullStyles.Reload();
            return OK("Theme and hull styles reloaded: {0}", The.UITheme.Id);
        }

        public override void Initialize() {
            Name = "theme";
            HelpLine = "Tools for changing and editing UI Themes";
        }
    }
}
