using System.Collections.Generic;
using KL.Utils;
using UnityEngine;

namespace Game.Visuals {
    // One region sky palette from Config/Visuals/SpaceSky. A def is either a difficulty
    // anchor (a sector between two anchors gets a blend of both, so every difficulty looks a
    // little different) or, when Templates is set, an override for those sector templates.
    // See Docs/Architecture/Systems/SpaceSky.md.
    public sealed class SpaceSkyDef {
        public const string ConfigPath = "Config/Visuals/SpaceSky";

        public string Id;
        // Difficulty anchor, ignored when Templates is set
        public float Difficulty;
        public string[] Templates;
        // Palette stops for thin, medium and dense cloud. Only hue and saturation are used on
        // the nebula (its brightness is kept), Highlight also tints the hull rim light.
        public string Shadow;
        public string Mid;
        public string Highlight;
        // 0 keeps the nebula exactly as authored, 1 fully recolors it
        public float Strength;
        public float Intensity = 1f;
        // Nebula brightness (peak channel) that maps to the Highlight stop
        public float BrightnessRange = 0.6f;
        public float RimStrength;
        // Optional SpaceSkyStormDef id that keeps playing while in this sector (template
        // overrides only), e.g. black hole flicker. Real storms take priority over it.
        // A template def without colors only adds this and keeps the difficulty palette.
        public string AmbientStorm;

        public bool HasPalette => !string.IsNullOrEmpty(Mid);

        private static List<SpaceSkyDef> anchors;
        private static Dictionary<string, SpaceSkyDef> overrides;

        [ExecuteOnReload]
        public static void Unload() {
            anchors = null;
            overrides = null;
        }

        private static void EnsureLoaded() {
            if (anchors != null) {
                return;
            }
            anchors = new List<SpaceSkyDef>();
            overrides = new Dictionary<string, SpaceSkyDef>();
            foreach (var def in The.ModLoader.LoadObjects<SpaceSkyDef>(ConfigPath)) {
                if (def.Templates != null && def.Templates.Length > 0) {
                    foreach (var tpl in def.Templates) {
                        overrides[tpl] = def;
                    }
                } else {
                    anchors.Add(def);
                }
            }
            anchors.Sort((a, b) => a.Difficulty.CompareTo(b.Difficulty));
        }

        public static bool TryGet(string id, out SpaceSkyDef def) {
            EnsureLoaded();
            foreach (var a in anchors) {
                if (a.Id == id) {
                    def = a;
                    return true;
                }
            }
            foreach (var o in overrides.Values) {
                if (o.Id == id) {
                    def = o;
                    return true;
                }
            }
            def = null;
            return false;
        }

        public static SpaceSkyPalette Resolve(float difficulty, string templateId) {
            EnsureLoaded();
            return Resolve(anchors, overrides, difficulty, templateId);
        }

        public static string ResolveAmbientStorm(string templateId) {
            EnsureLoaded();
            if (templateId != null && overrides.TryGetValue(templateId, out var over)
                    && !string.IsNullOrEmpty(over.AmbientStorm)) {
                return over.AmbientStorm;
            }
            return null;
        }

        public static SpaceSkyPalette Resolve(IReadOnlyList<SpaceSkyDef> sortedAnchors,
                IReadOnlyDictionary<string, SpaceSkyDef> templateOverrides,
                float difficulty, string templateId) {
            if (templateId != null && templateOverrides != null
                    && templateOverrides.TryGetValue(templateId, out var over)
                    && over.HasPalette) {
                return over.Palette;
            }
            if (sortedAnchors == null || sortedAnchors.Count == 0) {
                return default;
            }
            var first = sortedAnchors[0];
            if (difficulty <= first.Difficulty) {
                return first.Palette;
            }
            for (int i = 1; i < sortedAnchors.Count; i++) {
                var hi = sortedAnchors[i];
                if (difficulty < hi.Difficulty) {
                    var lo = sortedAnchors[i - 1];
                    var t = Mathf.InverseLerp(lo.Difficulty, hi.Difficulty, difficulty);
                    return SpaceSkyPalette.Lerp(lo.Palette, hi.Palette, t);
                }
            }
            return sortedAnchors[sortedAnchors.Count - 1].Palette;
        }

        private bool isPaletteParsed;
        private SpaceSkyPalette palette;
        public SpaceSkyPalette Palette {
            get {
                if (!isPaletteParsed) {
                    isPaletteParsed = true;
                    palette = new SpaceSkyPalette {
                        Shadow = ParseColor(Shadow),
                        Mid = ParseColor(Mid),
                        Highlight = ParseColor(Highlight),
                        Params = new Vector4(Strength, Intensity, BrightnessRange, RimStrength),
                    };
                }
                return palette;
            }
        }

        private Color ParseColor(string hex) {
            if (string.IsNullOrEmpty(hex)) {
                return Color.black;
            }
            if (Colors.TryFromHex(hex, out var color)) {
                return color;
            }
            D.Err("Invalid color in space sky {0} (should be standard hex format): {1}", Id, hex);
            return Color.black;
        }

        public override string ToString() {
            return $"SpaceSkyDef[{Id}]";
        }
    }
}
