using UnityEngine;
using KL.Console;
using Game.Commands;
using Game.Utils;
using Game.CodeGen;
using System.Text;
using System.IO;
using Game.Data;
using UnityEngine.SceneManagement;
using System.Linq;
using KL.Utils;

namespace Game.Console {
    public class ConsoleCommandLoadSave : ConsoleCommand {
        [SelfInit]
        static void Load() {
            Register(new ConsoleCommandLoadSave());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            string filename;
            if (args.HasArgument(1)) {
                filename = string.Join(" ", args.ArgumentsWithoutCommand);
            } else {
                args.Context.Output.Clear();
                var sb = new StringBuilder($"Available Saves: {SaveLoadUtils.AvailableSaves.Count}");
                foreach (var s in SaveLoadUtils.SavesByGame) {
                    var longest = s.Value.OrderByDescending(v => v.Playtime).FirstOrDefault();
                    sb.AppendFormat("[{2}] {0}/{1}\n", s.Key, longest.SaveName,
                        Units.SecondsToShortTime(longest.Playtime));
                }
                return OK(sb.ToString());
            }

            var meta = SaveLoadUtils.Find(filename);
            if (meta != null) {
                SaveLoadUtils.LoadSave(meta);
                return Close();
            }

            return Error($"Save not found: {filename}.\nUse <TAB> to autocomplete, or run 'load_save' without arguments to get a list of available saves.");
        }


        public override string Autocomplete(ConsoleCommandArguments args) {
            var saveName = string.Concat(args.ArgumentsWithoutCommand)
                .ToLowerInvariant().Replace(" ", "");
            foreach (var s in SaveLoadUtils.AvailableSaves) {
                var save = $"{s.GameName}{s.SaveName}".ToLowerInvariant().Replace(" ", "").Replace("_", "");
                if (save.StartsWith(saveName) || save.Contains(saveName)) {
                    return $"{s.GameName}/{s.SaveName}";
                }
            }
            return null;
        }

        public override void Initialize() {
            Name = "load_save";
            HelpLine = "Loads the save file by Game Name/Save Name";
        }
    }
}
