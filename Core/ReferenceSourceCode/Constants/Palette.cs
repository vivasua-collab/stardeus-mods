using System;
using System.Collections.Generic;
using Game.Data;
using KL.Collections;
using Game.Utils;
using Game.CodeGen;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using KL.Randomness;

namespace Game.Constants {
    [Serializable]
    public static class Palette {
        // TODO -> code gen ints for color ids and groups
        private static readonly Dictionary<string, ColorDef> All = new();
        public static readonly List<ColorDef> Generic = new();

        private static readonly Dictionary<string, List<ColorDef>> ByGroup = new();
        private static readonly Dictionary<string, Dictionary<ColorDef, float>> ByGroupWeighted = new();

        private static bool hasLoaded;
        private static Texture2D paletteAtlas;

        public static readonly Color White = Color.white;

        [SelfInit(SelfInit.Stage.AfterMods)]
        private static void Init() {
            // We want them to be fresh in unity
            All.Clear();
            ByGroup.Clear();
            Generic.Clear();
            hasLoaded = false;
            Load();
        }

        public static void Cleanup() {
            if (paletteAtlas != null) {
                The.Alloc.UnregisterAndDispose(paletteAtlas);
                paletteAtlas = null;
            }
        }

        public static bool HasGroup(string groupId) {
            if (groupId == null) {
                D.Err("Trying to get Palette.HasGroup with null groupId");
                return false;
            }
            return ByGroup.ContainsKey(groupId);
        }

        public static ColorDef RandomForGroup(string groupId, Rng rng) {
            if (ByGroupWeighted.TryGetValue(groupId, out var gw)) {
                if (rng.TryWeightedFrom(gw, out var col)) {
                    return col;
                }
                var g = ForGroup(groupId);
                if (g != null) {
                    return rng.From(g);
                }
            }
            return null;
        }

        public static List<ColorDef> ForGroup(string groupId) {
            return ByGroup.Get(groupId, null);
        }

        public static void Load() {
            if (hasLoaded) { return; }
            hasLoaded = true;

            var colors = The.ModLoader.LoadObjects<ColorDef>("Config/Colors");
            paletteAtlas = new Texture2D(colors.Count, 1);
            paletteAtlas.filterMode = FilterMode.Point;
            paletteAtlas.name = "PaletteAtlas";
            The.Alloc.Register(paletteAtlas);
            int atlasIdx = 0;
            for (int i = 0; i < colors.Count; i++) {
                ColorDef c = colors[i];
                if (!Arrays.IsEmpty(c.Groups)) {
                    foreach (var g in c.Groups) {
                        var w = c.Weight;
                        if (w <= 0f) {
                            w = 1f;
                        }
                        if (!ByGroup.TryGetValue(g, out var group)) {
                            group = new();
                            ByGroup.Add(g, group);
                        }
                        group.Add(c);
                        if (!ByGroupWeighted.TryGetValue(g, out var groupW)) {
                            groupW = new();
                            ByGroupWeighted.Add(g, groupW);
                        }
                        groupW.Add(c, w);
                    }
                }
                var old = All.PutGet(c.Id, c);
                if (old != null) {
                    D.Err("Palette already has color with same id: {0}!", c.Id);
                }
                if (c.IsGeneric) {
                    Generic.Add(c);
                }
                ParseColorDef(c);
                if (!c.SkipAtlas)  {
                    CreateSpriteFor(paletteAtlas, c, atlasIdx++);
                }
            }
            // link aliases
            for (int i = 0; i < colors.Count; i++) {
                ColorDef def = colors[i];
                if (def.AliasFor != null) {
                    if (All.TryGetValue(def.AliasFor, out var aliased)) {
                        var c = aliased.Color;
                        if (def.Alpha > 0) { c.a = def.Alpha; }
                        def.Color = c;
                    } else {
                        def.IsBroken = true;
                        def.Color = Color.magenta;
                        D.Err("Could not find aliased color by id for color: {0} -> {1}",
                            def.Id, def.AliasFor);
                    }
                }
            }
            paletteAtlas.Apply(false, true);
        }

        private static void ParseColorDef(ColorDef c) {
            if (!string.IsNullOrWhiteSpace(c.AliasFor)) {
                c.SkipAtlas = true;
                // this color is an alias
                return;
            } else {
                c.AliasFor = null;
            }
            if (Colors.TryFromHex(c.Hex, out Color color)) {
                if (c.Alpha > 0) { color.a = c.Alpha; }
                c.Color = color;
                return;
            }
            if (!Arrays.IsEmpty(c.Data) && c.Data.Length == 4) {
                color = Colors.FromData(c.Data);
                c.Color = color;
                return;
            }
            c.SkipAtlas = true;
            c.IsBroken = true;
            c.Color = Color.magenta;
            D.Warn("Cannot parse color def: {0}. If you pass Data, it has to contain 4 values (0-255)", c.Id);
        }

        private static void CreateSpriteFor(Texture2D atlas, ColorDef c, int i) {
            atlas.SetPixel(i, 0, c.Color);
            var sd = new SpriteData();
            sd.AtlasId = "PaletteAtlas";
            sd.AtlasHash = Hashes.S("PaletteAtlas");
            sd.Atlas = atlas;
            sd.Width = 1;
            sd.Height = 1;
            var texelSize = 1 / (float) atlas.width;
            sd.SetUV(new(i / (float) atlas.width, 0, texelSize, texelSize));
            sd.Id = $"color.{c.Id}";
            sd.IdH = Hashes.S(sd.Id);
            c.SpriteId = sd.Id;
            sd.SetSprite(Sprite.Create(atlas, new(i, 0, 1, 1),
                Vector2.zero, 1f, 0, SpriteMeshType.FullRect, Vector4.zero, false));
            RenderingService.Sprites.Store(sd);
        }

        public static ColorDef Get(string id) {
            if (id == null) {
                D.Err("Getting color by id null");
                id = "White";
            }
            var col = All.Get(id, null);
            D.Ass(col != null,
                "Color Type {0} is not defined! See Config/Colors folder",
                    id);
            if (col.IsBroken) {
                D.Err("Broken color by id: {0}", id);
            }
            return col;
        }

        public static bool HasColor(string id) {
            if (id == null) { return false; }
            return All.ContainsKey(id);
        }

        public static bool TryColorById(string id, out Color color) {
            if (id == null) {
                color = default;
                return false;
            }
            var colorDef = All.Get(id, null);
            if (colorDef == null) {
                color = default;
                return false;
            }
            if (colorDef.IsBroken) {
                D.Err("Broken color by id: {0}", id);
            }
            color = colorDef.Color;
            return true;
        }

        public static Color ItemColor(string id) {
            if (id == null) {
                return White;
            }
            var colorDef = All.Get(id, null);
            if (colorDef == null) {
                return White;
            }
            if (colorDef.IsBroken) {
                D.Err("Broken color by id: {0}", id);
            }
            return colorDef.Color;
        }
        [Obsolete("use themes")]
        public static Color ColorById(string id) {
            if (id == null) {
                return White;
            }
            var colorDef = All.Get(id, null);
            if (colorDef == null) {
                return White;
            }
            if (colorDef.IsBroken) {
                D.Err("Broken color by id: {0}", id);
            }
            return colorDef.Color;
        }

        public static string TMPColorById(string id) {
            var color = ColorById(id);
            return Colors.ToHex(color, '#');
        }
    }
}
