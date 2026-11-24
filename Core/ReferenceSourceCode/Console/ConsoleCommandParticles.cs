using System.Collections.Generic;
using Game.UI;
using Game.Visuals;
using KL.Console;
using UnityEngine;

namespace Game.Console {
    public class ConsoleCommandParticles : BaseConsoleCommand {
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Load() {
            Register(new ConsoleCommandParticles());
        }

        protected override HashSet<string> commandsThatDontBlockAchievements => new() {
            "debug"
        };

        protected override Dictionary<string, string> subCommands => new() {
            {"debug", "ShowDebug"},
        };

        protected ConsoleCommandResult ShowDebug(ConsoleCommandArguments args) {
            DebugOutputWidget.SpawnFor(new ParticleBudgetDebugOutput());
            return Close();
        }

        public override void Initialize() {
            Name = "particles";
            HelpLine = "Particle budget debugging";
        }
    }
}
