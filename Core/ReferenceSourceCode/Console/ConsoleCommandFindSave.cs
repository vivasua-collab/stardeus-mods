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
using System;

namespace Game.Console {
    public class ConsoleCommandFindSave : ConsoleCommand {
        [SelfInit]
        static void Load() {
            Register(new ConsoleCommandFindSave());
        }

        public override ConsoleCommandResult Execute(ConsoleCommandArguments args) {
            string filename = null;
            if (args.HasArgument(1)) {
                filename = string.Join(" ", args.ArgumentsWithoutCommand);
            }
            if (filename == null) {
                var sb = new StringBuilder($"Available Saves: {SaveLoadUtils.AvailableSaves.Count}\n");
                foreach (var s in SaveLoadUtils.SavesByGame) {
                    var longest = s.Value.OrderByDescending(v => v.Playtime).FirstOrDefault();
                    sb.AppendFormat("[{2}] {0}/{1}\n", s.Key, longest.SaveName,
                        Units.SecondsToShortTime(longest.Playtime));
                }
                return OK(sb.ToString());
            } else if (filename == "newest") {
                var sb = new StringBuilder("Newest Saves:\n");
                foreach (var s in SaveLoadUtils.SavesByGame
                    // at least 3h
                    .Where(s => s.Value.Max(v => v.Playtime) > 60 * 60 * 3)
                    .OrderBy(s => s.Value.Max(v => v.SaveTime))) {
                    var longest = s.Value.OrderByDescending(v => v.SaveTime).FirstOrDefault();
                    if (longest.Playtime > 60 * 60 * 3) {
                        sb.AppendFormat("[{2}/{3}] {0}/{1}\n", s.Key, longest.SaveName,
                            Units.SecondsToShortTime(longest.Playtime),
                            DateTime.FromFileTimeUtc(longest.SaveTime).ToShortDateString());
                    }
                }
                return OK(sb.ToString());
            } else if (filename == "longest") {
                var sb = new StringBuilder("Longest Saves:\n");
                foreach (var s in SaveLoadUtils.SavesByGame.OrderBy(s => s.Value.Max(v => v.Playtime))) {
                    var longest = s.Value.OrderByDescending(v => v.Playtime).FirstOrDefault();
                    if (longest.Playtime > 60 * 60) {
                        sb.AppendFormat("[{2}] {0}/{1}\n", s.Key, longest.SaveName,
                            Units.SecondsToShortTime(longest.Playtime));
                    }
                }
                return OK(sb.ToString());
            }


            var sb2 = new StringBuilder("Similar Saves:\n");
            var now = DateTime.Now;
            bool found = false;
            foreach (var s in SaveLoadUtils.AvailableSaves) {
                var save = $"{s.GameName}{s.SaveName}".ToLowerInvariant().Replace(" ", "").Replace("_", "");
                if (save.StartsWith(filename) || save.Contains(filename)) {
                    found = true;
                    sb2.AppendFormat("[{2}] {0}/{1} ({3})\n", s.GameName, s.SaveName,
                        Units.SecondsToShortTime(s.Playtime),
                        $"{Math.Round((now - s.LastWriteTime).TotalDays, 2)} days ago");
                }
            }
            if (found) {
                return OK(sb2.ToString());
            } else {
                return Error("Found nothing. Try without arguments or 'find_save longest'");
            }
        }

        public override string Autocomplete(ConsoleCommandArguments args) {
            var saveName = string.Concat(args.ArgumentsWithoutCommand)
                .ToLowerInvariant().Replace(" ", "");
            foreach (var s in SaveLoadUtils.AvailableSaves) {
                var save = s.SaveName.ToLowerInvariant().Replace(" ", "").Replace("_", "");
                if (save.StartsWith(saveName) || save.Contains(saveName)) {
                    return $"{s.GameName}/{s.SaveName}";
                }
            }
            return null;
        }

        public override void Initialize() {
            Name = "find_save";
            HelpLine = "Finds the save file by name";
        }
    }
}
