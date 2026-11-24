using System;
using System.Collections.Generic;
using System.IO;
using Game.UI;
using KL.Utils;
using UnityEngine;

namespace Game.Mods {
    // Mod versions the game refuses to load: they break the game or no longer load against it.
    //
    // WHY a file next to AssetsManifest.json instead of a list in code: the Workshop validation
    // tool (.claude/skills/stardeus-mod-validate) rewrites it after testing every mod, and the
    // diff is reviewed like any other data change. It ships with the build because a game update
    // is what breaks mods. A mod is matched by its ModInfo Id: installed Workshop items rarely
    // carry their SteamWorkshopItemId file, so the workshop id cannot be relied on in game.
    public static class DisabledMods {
        public const string FileName = "DisabledMods.json";

        [Serializable]
        public class Entry {
            public string Id;
            // This version and every older one; empty means every version
            public string Version;
            // Added by hand: the validation tool never removes it
            public bool Manual;
            // For people reading the diff, the game ignores these
            public string WorkshopId;
            public string Reason;
            public string Checked;
        }

        [Serializable]
        public class Data {
            public List<Entry> Mods = new();
        }

        private static Dictionary<string, Entry> byId;

        public static string DefaultPath => Path.Combine(Application.streamingAssetsPath, FileName);

        public static void Load() {
            Load(DefaultPath);
        }

        // A missing or broken file disables nothing: refusing every mod over a bad list would
        // be worse than loading a few broken ones
        public static void Load(string path) {
            byId = new Dictionary<string, Entry>();
            var json = Files.ReadJSONWithComments(path);
            if (json == null) {
                D.Err("Disabled mods list not found: {0}", path);
                return;
            }
            if (!Json.TryDeserialize(json, out Data data, out var error) || data?.Mods == null) {
                D.Err("Failed reading disabled mods list {0}: {1}", path, error);
                return;
            }
            foreach (var entry in data.Mods) {
                if (string.IsNullOrWhiteSpace(entry?.Id)) { continue; }
                if (!byId.TryAdd(entry.Id, entry)) {
                    D.Err("Disabled mods list has {0} twice, using the first entry", entry.Id);
                }
            }
        }

        // maxVersion is the newest disabled version, empty when every version is
        public static bool IsDisabled(string id, string modVersion, out string maxVersion) {
            if (byId == null) {
                Load();
            }
            maxVersion = null;
            if (string.IsNullOrEmpty(id) || !byId.TryGetValue(id, out var entry)) {
                return false;
            }
            maxVersion = entry.Version ?? string.Empty;
            return Covers(entry, modVersion);
        }

        public static bool Covers(Entry entry, string modVersion) {
            if (entry == null) { return false; }
            var maxVersion = entry.Version ?? string.Empty;
            if (maxVersion.Length == 0) {
                return true;
            }
            return !VersionInfo.IsVersionNewerWithRC(modVersion, maxVersion);
        }
    }
}
