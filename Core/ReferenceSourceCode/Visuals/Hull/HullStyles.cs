using System.Collections.Generic;
using Game.Data.Space;
using KL.Utils;

namespace Game.Visuals.Hull {
    // The hull view styles: one HullStyleDef per tileset, from HullStyle.json next to its art
    // (Graphics/Structure/Hull/<Tileset>/), so a hull mod is one folder of art and numbers. Read
    // once, and again on `theme reload`. A style is layered: the code defaults, then Default's
    // file, then its own, so a style file lists only what it changes and tuning Default's file
    // carries over to every value another style leaves alone. Which style a ship wears is saved
    // on the ship's space object.
    public static class HullStyles {
        public const string Folder = "Graphics/Structure/Hull";
        public const string FileName = "HullStyle.json";
        private const string ShipKey = "HullStyle";

        private static Dictionary<string, HullStyleDef> styles;
        private static HullStyleDef fallback;

        // Bumped on every load, so the hull view re-bakes with the new values
        public static int Version { get; private set; }

        [ExecuteOnReload]
        public static void Unload() {
            styles = null;
            fallback = null;
        }

        public static void Reload() {
            Unload();
            EnsureLoaded();
        }

        // The style of a tileset; Default's for one without a style file
        public static HullStyleDef Get(string tileset) {
            EnsureLoaded();
            if (tileset != null && styles.TryGetValue(tileset, out var style)) {
                return style;
            }
            return fallback;
        }

        // The tileset the ship's hull is drawn with, as saved (its art may not be installed)
        public static string TilesetOf(SpaceObject ship) {
            if (ship == null || !ship.HasData) { return HullDetails.DefaultTileset; }
            return ship.Data.GetString(ShipKey, HullDetails.DefaultTileset);
        }

        public static void SetTileset(SpaceObject ship, string tileset) {
            // Default is not stored, so a ship that never changed it saves nothing
            ship.Data.SetString(ShipKey,
                tileset == HullDetails.DefaultTileset ? null : tileset);
        }

        private static void EnsureLoaded() {
            if (styles != null) { return; }
            styles = new Dictionary<string, HullStyleDef>();
            Version++;
            var files = new Dictionary<string, string>();
            foreach (var kv in The.ModLoader.FindObjectFiles(Folder)) {
                files[Files.ToForwardSlashes(kv.Key)] = kv.Value;
            }
            string baseJson = null;
            string basePath = null;
            if (files.TryGetValue($"{HullDetails.DefaultTileset}/{FileName}", out basePath)) {
                baseJson = The.ModLoader.LoadPatchedJson(basePath);
            }
            fallback = Build(baseJson, basePath, null, null);
            styles[HullDetails.DefaultTileset] = fallback;
            foreach (var kv in files) {
                int slash = kv.Key.LastIndexOf('/');
                if (slash <= 0 || kv.Key.Substring(slash + 1) != FileName) { continue; }
                var tileset = kv.Key.Substring(0, slash);
                if (tileset == HullDetails.DefaultTileset) { continue; }
                styles[tileset] = Build(baseJson, basePath,
                    The.ModLoader.LoadPatchedJson(kv.Value), kv.Value);
            }
        }

        private static HullStyleDef Build(string baseJson, string basePath, string json,
                string path) {
            var style = new HullStyleDef();
            Apply(ref style, baseJson, basePath);
            Apply(ref style, json, path);
            style.Sanitize();
            return style;
        }

        private static void Apply(ref HullStyleDef style, string json, string path) {
            if (string.IsNullOrEmpty(json)) { return; }
            // Strict first, so a misspelt field is reported; then soft, so the rest of the file
            // still applies
            if (!Json.Deserialize(json, path, ref style)) {
                Json.Deserialize(json, path, ref style, strict: false);
            }
        }
    }
}
