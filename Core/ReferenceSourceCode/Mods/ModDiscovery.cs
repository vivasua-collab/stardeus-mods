#if PRODUCTION_BUILD
#define VERBOSE_MODS
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Game.Constants;
using Game.UI;
using Game.Systems.UIUX;
using KL.Utils;

namespace Game.Mods {
    public sealed class ModDiscovery {
        private readonly List<string> rootFolders = new();
        public readonly List<string> Available = new();
        public readonly List<string> SharedSaves = new();
        private readonly List<string> uploadableRoots = new();
        private readonly HashSet<int> workshop = new();

        public void AddRoot(string root, bool uploadable) {
            rootFolders.Add(root);
            if (uploadable) {
                uploadableRoots.Add(root);
            }
            Files.EnsureDir(root);
        }

        public bool IsUploadable(string modPath) {
            foreach (var root in uploadableRoots) {
                if (modPath.StartsWith(root)) {
                    return true;
                }
            }
            return false;
        }

        public void RegisterMod(string path) {
            #if VERBOSE_MODS
            D.Warn("Registering mod: {0}", path);
            #endif
            Available.Add(path);
        }

        private bool LookForModAt(string modFolder) {
            var directoryName = Path.GetFileName(modFolder);
            if (directoryName.StartsWith(".")) {
                return false;
            }
            if (directoryName.EndsWith("_DISABLED", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            var modInfoPath = $"{modFolder}/ModInfo.json";
            if (File.Exists(modInfoPath)) {
                #if VERBOSE_MODS
                D.Warn("Adding available mod: {0}", modFolder);
                #endif
                Available.Add(modFolder);
                return true;
            }
            if (File.Exists($"{modFolder}/SaveInfo.json")) {
                // shared save file
                SharedSaves.Add(modFolder);
                return true;
            }
            // TODO remove after a while when all playtesters have updated
            if (modFolder.EndsWith("Template")) {
                Files.ClearDirectory(modFolder, true);
            } else {
                D.Err("Skipping mod folder without ModInfo.json: {0}", modFolder);
                Ready.WhenCore(() => {
                    WPopup.Create(true, IconId.CWarning, $"SKIPPING MOD {modFolder}",
                        $"{modInfoPath} does not exist")
                        .WithAction2(T.OpenDir, () => {
                            Files.OpenOSFolder(modFolder);
                        })
                        .WithAction(T.Ok, null)
                        .Spawn();
                });
            }
            return false;
        }

        public void CollectMods() {
            #if VERBOSE_MODS
            D.Warn("Mod root folders: {0}", string.Join(", ", rootFolders));
            #endif
            foreach (var modRoot in rootFolders) {
                var modDirs = Directory.EnumerateDirectories(modRoot);
                #if VERBOSE_MODS
                D.Warn("Mod dirs in {0}: {1}", modRoot, string.Join(", ", modDirs));
                #endif
                foreach (var modDir in modDirs) {
                    LookForModAt(modDir);
                }
            }
            #if !PRODUCTION_BUILD
            if (ModValidationRun.IsActive) {
                // only the folders the validation tool picked, never the Steam user's
                // subscriptions; a ship lands in SharedSaves through its SaveInfo.json
                foreach (var folder in ModValidationRun.Spec.Mods) {
                    // Workshop downloads: loaded as players get them, with the lenient JSON
                    // reading the game gives Workshop items
                    if (LookForModAt(folder)) {
                        workshop.Add(Hashes.S(folder));
                    }
                }
                if (!string.IsNullOrEmpty(ModValidationRun.Spec.Ship)) {
                    LookForModAt(ModValidationRun.Spec.Ship);
                }
                return;
            }
            #endif
            #if !DISABLESTEAMWORKS
            AppendSteamMods();
            #endif
        }

        public bool IsWorkshop(string path) {
            return workshop.Contains(Hashes.S(path));
        }
    }
}
