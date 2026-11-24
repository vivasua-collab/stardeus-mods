using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using KL.Randomness;
using Game.Rendering;
using KL.Utils;
using UnityEngine;
using Unity.Mathematics;
using System;

namespace Game.Utils {
    public static class Images {
        [SelfInit]
        private static void Init() {
            MissingIcon = Res.LoadSprite("Images/Misc/Missing");
            Transparent = Res.LoadSprite("Images/Misc/TransparentPixel");
            cursorCache.Clear();
            cursorSpriteCache.Clear();
            matchingCache.Clear();
            trimmedPatternCache.Clear();
        }
        private const int udbImageFrameWidth = 4;
        private const int udbImageFrameHeight = 2;
        public static Sprite MissingIcon;
        public static Sprite Transparent;
        private static readonly Dictionary<string, Texture2D> cursorCache = new();
        private static readonly Dictionary<string, Sprite> cursorSpriteCache = new();
        private static readonly Dictionary<string, List<string>> matchingCache = new();
        private static readonly Dictionary<int, string> trimmedPatternCache = new();

        public static Texture2D Cursor(string type) {
            if (cursorCache.TryGetValue(type, out var tex)) {
                return tex;
            }
            var texUncached = Resources.Load<Texture2D>($"Images/Cursors/{type}");
            if (texUncached == null) {
                D.Err("Cursor image not found: Images/Cursors/{0}", type);
            }
            cursorCache.Add(type, texUncached);
            return texUncached;
        }
        public static Sprite CursorSprite(string type) {
            if (cursorSpriteCache.TryGetValue(type, out var tex)) {
                return tex;
            }
            var texUncached = Res.LoadSprite($"Images/Cursors/{type}@2x");
            cursorSpriteCache.Add(type, texUncached);
            return texUncached;
        }

        public static bool SpriteExists(string id) {
            return RenderingService.Sprites.Contains(id);
        }

        public static bool TrySprite(string id, out Sprite sprite) {
            if (id == null || RenderingService.Sprites == null) {
                sprite = null;
                return false;
            }
            if (RenderingService.Sprites.TryFind(id, out var sd)) {
                sprite = sd.Sprite;
                return true;
            }
            sprite = null;
            return false;
        }

        public static string TrimPattern(string pattern, int endLength) {
            var hash = Hashes.S(pattern);
            hash = Hashes.Quick(hash, endLength);
            if (!trimmedPatternCache.TryGetValue(hash, out var trimmed)) {
                trimmed = pattern.Remove(pattern.Length - endLength, endLength);
                trimmedPatternCache[hash] = trimmed;
            }
            return trimmed;
        }

        public static string RandomMatching(string pattern, int endLength) {
            return RandomMatching(pattern, endLength, Rng.Unseeded);
        }
        public static string RandomMatching(string pattern, int endLength,
                Rng rng) {
            return rng.From(Matching(TrimPattern(pattern, endLength)));
        }
        public static string RandomMatching(string pattern, Rng rng) {
            return rng.From(Matching(pattern));
        }
        public static string RandomMatching(string pattern) {
            return Rng.UFrom(Matching(pattern));
        }

        public static List<string> Matching(string pattern) {
            if (matchingCache.TryGetValue(pattern, out var res)) {
                return res;
            }
            res = new List<string>();
            foreach (var sprite in RenderingService.Sprites.AllIds) {
                if (sprite.StartsWith(pattern)) {
                    res.Add(sprite);
                }
            }
            res.Sort();
            matchingCache.Add(pattern, res);
            return res;
        }

        public static Sprite SpriteWithResourceFallback(string id) {
            if (TrySprite(id, out var sprite)) {
                return sprite;
            }
            sprite = Res.LoadSprite(id);
            if (sprite == null) {
                D.Err("Sprite not found in resources: {0}", id);
            }
            return sprite;
        }

        public static Sprite Sprite(string id) {
            if (id == null) {
                D.Err("Trying to get sprite by null id!");
                return null;
            }
            if (RenderingService.Sprites.TryFind(id, out var sd)) {
                return sd.Sprite;
            }
            if (id == string.Empty) {
                D.Err("Trying to get an empty icon id");
                return null;
            }
            D.ErrSoft("Icon missing: {0}", id);
            return MissingIcon;
            /*
            var fallbackSd =  RenderingService.Sprites.FindH(IconIdH.CMissing);
            if (fallbackSd == null) {
                D.Err("Could not load missing fallback icon. We probably are too early in the load stage");
                return null;
            }
            return fallbackSd.Sprite;
            */
        }

        public static Sprite SpriteWithBorders(string id, int4 b, string ctx) {
            if (b.x == 0 && b.y == 0 && b.z == 0 && b.w == 0) {
                D.Err("Trying to get a sprite with 0 borders {0}. Context: {1}",
                    id, ctx);
                return Sprite(id);
            }
            // 1. find image by id + borders hash
            var hash = Hashes.S(id);
            hash = Hashes.Quick(hash, b.GetHashCode());
            if (RenderingService.Sprites.TryFindHash(hash, out var sd)) {
                return sd.Sprite;
            }
            // 2. create a version
            if (RenderingService.Sprites.TryFind(id, out sd)) {
                var o = sd.Sprite;
                var copy = sd.Copy();
                var border = new Vector4(b.x, b.y, b.z, b.w);
                var modified = UnityEngine.Sprite.Create(
                    o.texture,
                    o.rect,
                    o.pivot,
                    o.pixelsPerUnit,
                    0,
                    SpriteMeshType.FullRect,
                    border,
                    false);
                copy.SetSprite(modified);
                RenderingService.Sprites.StoreWithHash(hash, copy);
                return modified;
            } else {
                D.Err("Cannot find sprite by id for sprite with borders: {0}. Context: {1}",
                    id, ctx);
                return null;
            }
        }
        public static float FindHeightForUI(float width, float height, float containerWidth, bool usePadding) {
            float x = width;
            float y = height;
            if (usePadding) {
                x += udbImageFrameHeight * 2;
                y += udbImageFrameWidth * 2;
            }

            var aspectRatio = x / y;
            // D.Warn("{0}x{1} -> {2}x{3} : {4}", width, height, x, y, aspectRatio);

            float h;
            if (x < containerWidth) {
                h = y;
            } else {
                h = containerWidth / aspectRatio;
            }

            h = math.min(h, width);
            return h;
        }

        public static float FindHeightForUI(Sprite sprite, float width) {
            var sr = sprite.rect;
            return FindHeightForUI(sr.width, sr.height, width, true);
        }
    }
}
