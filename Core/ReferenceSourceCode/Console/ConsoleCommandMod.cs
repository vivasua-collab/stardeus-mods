#if !STARDEUS_DEMO
using System.Collections.Generic;
using System.IO;
using Game.Data;
using Game.Platform.Steam;
using Game.Utils;
using KL.Console;
using KL.Utils;
using UnityEngine;

namespace Game.Console {
    public class ConsoleCommandMod : BaseConsoleCommand {
        [SelfInit]
        private static void Load() {
            Register(new ConsoleCommandMod());
        }
        protected override HashSet<string> commandsThatDontBlockAchievements =>
            new HashSet<string>() {
                "create",
                "debug_prepare",
                "debug_upload_prepared",
            };

        public override void Initialize() {
            Name = "mod";
            HelpLine = "Tools for creating and uploading mods";
        }
        protected override Dictionary<string, string> subCommands => new() {
            { "create", "Create" },
            { "debug_prepare", "Prepare" },
            { "debug_upload_prepared", "UploadPrepared" },
        };
        private ConsoleCommandResult Create(ConsoleCommandArguments args) {
            if (!args.HasArgument(2)) {
                return Error("Mod ID is required");
            }
            if (!args.HasArgument(3)) {
                return Error("Mod name is required");
            }

            var modId = Strings.RemovePathChars(args.GetString(2));
            modId = Files.SanitizeFileName(modId);
            modId = Strings.CapitalizeFirst(modId);
            if (string.IsNullOrWhiteSpace(modId)) {
                return Error("Mod ID is empty after sanitization");
            }

            var modName = Strings.CapitalizeFirst(args.GetString(3, true));
            var modPath = Persist.PersistentPath("Mods", modId);
            if (Directory.Exists(modPath)) {
                return Error("Mod directory already exists: {0}", modPath);
            }

            var info = new ModInfo {
                Id = modId,
                Name = modName,
                GameVersion = Game.Constants.Consts.ModsVersionTag.Replace("v", string.Empty),
                ModVersion = "1.0.0",
            };
            var json = JsonUtility.ToJson(info, true);
            if (!Files.EnsureDir(modPath)) {
                return Error("Failed to create mod directory: {0}", modPath);
            }

            var writeResult = Persist.Write(modPath, "ModInfo.json", json, false);
            if (!writeResult.IsSuccess) {
                RemoveEmptyDirectory(modPath);
                return Error("Failed to create ModInfo.json: {0}", writeResult.Exception);
            }

            Files.OpenOSFolder(modPath);
            return OK("Created mod: {0}", modId);
        }
        private static void RemoveEmptyDirectory(string path) {
            try {
                if (Directory.Exists(path) && Files.IsDirEmpty(path)) {
                    Directory.Delete(path);
                }
            } catch (System.Exception ex) {
                D.LogEx(ex, "Failed to remove empty mod directory: {0}", path);
            }
        }
        private ConsoleCommandResult Prepare(ConsoleCommandArguments args) {
            if (The.Platform is not SteamPlatform steam) {
                return Error("Steam not available");
            }
            var modId = args.GetString(2);
            if (!The.ModLoader.ModInfos.TryGetValue(modId, out var mod)) {
                return Error("Mod not found: {0}", modId);
            }
            if (steam.Workshop.PrepareForUpload(mod)) {
                Files.OpenOSFolder(steam.Workshop.ModTempPath);
                return OK("Prepared mod: {0}", modId);
            } else {
                return Error("Failed to prepare mod: {0}", modId);
            }
        }
        private ConsoleCommandResult UploadPrepared(ConsoleCommandArguments args) {
            if (The.Platform is not SteamPlatform steam) {
                return Error("Steam not available");
            }
            var modId = args.GetString(2);
            if (!The.ModLoader.ModInfos.TryGetValue(modId, out var mod)) {
                return Error("Mod not found: {0}", modId);
            }
            steam.Workshop.CreateOrUpdate(mod, prepare: false);
            return OK("Uploading mod: {0}", modId);
        }
    }
}
#endif
