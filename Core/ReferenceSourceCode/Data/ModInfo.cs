#if !PRODUCTION_BUILD
// #define VERBOSE_PATH
#endif
using System;
using System.IO;
using Game.Constants;
using Game.Input;
using Game.Mods;
using Game.Rendering;
using Game.Utils;
using KL.Utils;

namespace Game.Data {
    [Serializable]
    public class ModInfo {
        public string Id;
        public string Name;
        public string Description;
        /// <summary>
        /// If this is empty, steam workshop description will be loaded from
        /// Description field. You can use [i], [b], [url] and similar tags
        /// in steam workshop description, but not in the game description field.
        /// </summary>
        public string DescriptionSteam;
        /// <summary>
        /// If set to true, steam workshop description will not be updated when
        /// new versions of the mod will get uploaded.
        /// </summary>
        public bool DoNotUpdateSteamDescription;
        /// <summary>
        /// Set to true if this mod is a cheat. While it is enabled, achievements
        /// are disabled, and any game played with it keeps them disabled even
        /// after the mod is removed. Uploads get the "Cheat" Steam Workshop tag.
        /// </summary>
        public bool Cheat;
        public bool SteamWorkshop;
        public string ModVersion;
        public string GameVersion;
        [NonSerialized]
        public bool IsDevDisabled;
        public bool IsRuntimeBroken { get; private set; }
        // The first reason it was marked broken for
        [NonSerialized]
        public string RuntimeBrokenReason;
        [NonSerialized]
        public bool HadWarnings;
        public string[] DLLFiles;
        public void MarkRuntimeBroken(string why) {
            if (Id == "Core" || Id == "Translations") {
                var msg = "Core mod is broken at runtime! Verify game file system integrity, check if antivirus has quarantined some files, try reinstalling the game, etc. Reason: {0}";
                D.Err(msg, why);
                return;
            }
            D.Err("Marking mod {0} [{1}] as broken at runtime! {2}",
                Id, ModVersion, why);
            IsRuntimeBroken = true;
            RuntimeBrokenReason ??= why;
        }
        [NonSerialized]
        public bool IsEnabled;
        private ulong steamWorkshopId;
        public ulong SteamWorkshopId {
            get {
                #if !DISABLESTEAMWORKS
                if (steamWorkshopId == 0) {
                    var rawId = Files.ReadText(ModFilePath(
                        Game.Platform.Steam.SteamWorkshopItem.WorkshipIdFileName,
                            isDirectory: false,
                            resolvePaths: false));
                    if (rawId != null) {
                        ulong.TryParse(rawId.Trim(), out steamWorkshopId);
                    }
                }
                #endif
                return steamWorkshopId;
            }
        }
        public string NameDecorated {
            get {
                var name = IsEnabled ? Name : Texts.Strike(Name);
                if (Cheat) {
                    name = $"{name} {CheatMods.Marker}";
                }
                if (SteamWorkshopId > 0) {
                    return $"{GlyphMap.SteamIcon} {name}";
                } else {
                    return name;
                }
            }
        }
        public string[] Dependencies;
        public AtlasInfo[] Atlases;
        [NonSerialized]
        public string FullPath;
        public bool IsCoreTranslation;
        public string CoverPath => ModFilePath("ModCover.jpg", false);

        public bool IdMatches(string id) {
            var matches = false;
            if (Id == id) {
                matches = true;
            } else if (SteamWorkshopId > 0) {
                matches = Id == $"{id}_{SteamWorkshopId}";
            }
            // D.Warn("Id matches: {0} -> {1} -> {2}", matches, id, this);
            return matches;
        }

        public string ModFile(string file) {
            return ModFilePath(file, false, true);
        }
        public string ModFile(string folder, string file) {
            return ModFilePath(folder, file, false, true);
        }

        public string ModFolder(string folder) {
            return ModFilePath(folder, true, true);
        }
        public string ModFolder(string folder, string folder2) {
            return ModFilePath(folder, folder2, true, true);
        }

        public string ModFilePath(string file, bool isDirectory, bool resolvePaths = true) {
            return ModFilePath(file, string.Empty, isDirectory, resolvePaths);
        }

        public string ModFilePath(string folder, string file, bool isDirectory,
                bool resolvePaths = true) {
            string p = null;
            if (resolvePaths) {
                foreach (string resolvePath in Consts.ModDirLoadOrder) {
                    p = Path.Combine(FullPath, resolvePath, folder, file);
                    if (isDirectory) {
                        if (Directory.Exists(p)) {
                            #if VERBOSE_PATH
                            D.Warn("Found mod directory: {0}", p);
                            #endif
                            return p;
                        }
                    } else {
                        if (File.Exists(p)) {
                            #if VERBOSE_PATH
                            D.Warn("Found mod file: {0}", p);
                            #endif
                            return p;
                        }
                    }
                    #if VERBOSE_PATH
                    if (isDirectory) {
                        D.Warn("Did not find mod dir: {0}", p);
                    } else {
                        D.Warn("Did not find mod file: {0}", p);
                    }
                    #endif
                }
                #if VERBOSE_PATH
                if (isDirectory) {
                    D.Warn("Directory does not exist! {0}", p);
                } else {
                    D.Warn("File does not exist! {0}", p);
                }
                #endif
            } else {
                p = Path.Combine(FullPath, folder, file);
            }
            return p;
        }

        public override string ToString() {
            return $"{Id} [{ModVersion}]";
        }
    }
}
