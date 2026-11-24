using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace Game.UI {
    // A def's own icon inside any text: <sprite="DefIcons" name="{def id}">. Def links get it
    // by themselves (Texts.LinkToCodex). The glyphs point into the sprite atlases the game
    // already built, so no pixels are copied: one sprite asset per atlas page, found through
    // the root asset by name. The tag names the def, not the page, so a string made in one
    // session still works when the atlases are packed differently (texture quality, mods).
    public static class DefIcons {
        public const string AssetName = "DefIcons";
        // The box of Glyphs_IconsColor, so def icons sit like the other inline glyphs
        private const float Box = 128f;
        private const float BoxTop = 112f;
        // Space between the icon and the name that follows it, in box units
        private const float Gap = 24f;
        private const string TemplatePath = "Sprite Assets/Glyphs_IconsColor";

        // TMP keeps the asset it got for a name for the whole process and cannot forget it,
        // and the Editor enters play mode without a domain reload. So the root lives as long
        // as the process and is never reset; only its pages are rebuilt.
        private static TMP_SpriteAsset root;
        private static bool isSubscribed;
        private static readonly List<TMP_SpriteAsset> pages = new();

        private static readonly Dictionary<int, string> tags = new();

        [ExecuteOnReload]
        private static void Reset() {
            tags.Clear();
        }

        public static string Tag(Def def) {
            if (def == null) { return string.Empty; }
            return Tag(def.IdH);
        }

        // Empty when the def has no icon
        public static string Tag(int defIdH) {
            return tags.TryGetValue(defIdH, out var tag) ? tag : string.Empty;
        }

        // After the defs and the graphics are loaded
        public static void Rebuild() {
            var template = Resources.Load<TMP_SpriteAsset>(TemplatePath);
            if (template == null) {
                D.Err("Def icons: no template sprite asset at {0}", TemplatePath);
                return;
            }
            EnsureRoot();
            DestroyPages();
            tags.Clear();
            var byAtlas = new Dictionary<Texture2D, TMP_SpriteAsset>();
            foreach (var def in The.Defs.Defs.Values) {
                def.ResetCodexLink();
                var sd = IconFor(def);
                if (sd == null || sd.Atlas == null) { continue; }
                if (!byAtlas.TryGetValue(sd.Atlas, out var page)) {
                    page = CreatePage(template, sd.Atlas);
                    byAtlas.Add(sd.Atlas, page);
                }
                AddGlyph(page, def, sd);
            }
            for (int i = 0; i < pages.Count; i++) {
                pages[i].UpdateLookupTables();
            }
            root.UpdateLookupTables();
        }

        // Def.PreviewSprite's choice, as the sprite data (keeps the single tile UV crop of
        // connected sprites) and without making Sprite objects
        private static SpriteData IconFor(Def def) {
            if (!string.IsNullOrWhiteSpace(def.PreviewIcon)) {
                return RenderingService.Sprites.TryFind(def.PreviewIcon, out var icon)
                    ? icon : null;
            }
            if (def.IsAbstract || def.Components == null) { return null; }
            for (int i = 0; i < def.Components.Length; i++) {
                var c = def.Components[i];
                if (c == null) { continue; }
                switch (c.Component) {
                    case "TileGraphics": {
                        var sd = TileGraphicsComp.IconSDFor(def.Id, c);
                        return sd == null || sd.Id == GraphicsId.IconsColorMissing ? null : sd;
                    }
                    case "ObjGraphics": {
                        return RenderingService.Sprites.TryFind(ObjGraphicsComp.IconFor(c),
                            out var sd) ? sd : null;
                    }
                    case "BeingGraphics":
                        // Creature defs have no icon of their own (PreviewSprite shows a human)
                        return null;
                }
            }
            return null;
        }

        private static void EnsureRoot() {
            if (root == null) {
                root = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                root.name = AssetName;
                root.hideFlags = HideFlags.HideAndDontSave;
                root.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            }
            if (!isSubscribed) {
                TMP_Text.OnSpriteAssetRequest += OnSpriteAssetRequest;
                isSubscribed = true;
            }
        }

        private static TMP_SpriteAsset OnSpriteAssetRequest(int hashCode, string name) {
            return name == AssetName ? root : null;
        }

        private static void DestroyPages() {
            root.fallbackSpriteAssets.Clear();
            for (int i = 0; i < pages.Count; i++) {
                var page = pages[i];
                if (page == null) { continue; }
                if (page.material != null) {
                    Object.Destroy(page.material);
                }
                Object.Destroy(page);
            }
            pages.Clear();
        }

        // A copy of a shipped glyph asset brings its version (a runtime asset without one is
        // "upgraded" by TMP, which empties its tables), its font-relative face and its shader
        private static TMP_SpriteAsset CreatePage(TMP_SpriteAsset template, Texture2D atlas) {
            var page = Object.Instantiate(template);
            page.name = $"{AssetName}_{pages.Count}";
            page.hideFlags = HideFlags.HideAndDontSave;
            page.spriteGlyphTable.Clear();
            page.spriteCharacterTable.Clear();
            page.fallbackSpriteAssets = null;
            page.spriteSheet = atlas;
            var material = new Material(template.material) {
                name = page.name,
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = atlas,
            };
            page.material = material;
            pages.Add(page);
            root.fallbackSpriteAssets.Add(page);
            return page;
        }

        private static void AddGlyph(TMP_SpriteAsset page, Def def, SpriteData sd) {
            var atlas = sd.Atlas;
            var uv = sd.UV;
            int x = Mathf.RoundToInt(uv.xMin * atlas.width);
            int y = Mathf.RoundToInt(uv.yMin * atlas.height);
            int w = Mathf.RoundToInt(uv.xMax * atlas.width) - x;
            int h = Mathf.RoundToInt(uv.yMax * atlas.height) - y;
            if (w <= 0 || h <= 0) { return; }
            // The longer side fills the box, centered
            float fit = Box / Mathf.Max(w, h);
            float gw = w * fit;
            float gh = h * fit;
            var metrics = new GlyphMetrics(gw, gh, (Box - gw) * 0.5f,
                BoxTop - (Box - gh) * 0.5f, Box + Gap);
            // TMP draws a glyph of an asset without face metrics as tall as the font's ascent
            // line; this scale brings the short side of a wide sprite back into the box
            var glyph = new TMP_SpriteGlyph((uint) page.spriteGlyphTable.Count, metrics,
                new GlyphRect(x, y, w, h), gh / Box, 0);
            page.spriteGlyphTable.Add(glyph);
            // 0xFFFE: no unicode, found by name only
            page.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, page, glyph) {
                name = def.Id,
            });
            tags[def.IdH] = $"<sprite=\"{AssetName}\" name=\"{def.Id}\">";
        }
    }
}
