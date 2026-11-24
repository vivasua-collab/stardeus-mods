using System.Collections.Generic;
using Game.Constants;
using Game.Rendering;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Areas {
    // The foundation strips floors name in their TileGraphics ("Foundation"), numbered for the
    // floor buffer (FloorKindBuffer) and described to Foundation.shader: where a strip sits in
    // the Hull atlas, how many tiles it spans and how deep it hangs. All strips share one
    // atlas so the foundation stays one draw call. A strip's source pixels are its size at
    // 256 px per tile, so what a modder draws is what the face shows at any texture quality.
    public sealed class FoundationStyles {
        public const int None = 0;
        // Floors that name no strip (mod floors that do not derive from BaseFloor, overlooked
        // defs) and strips that cannot be drawn get this one, so every floor has a foundation
        public const string FallbackId = "Structure/Foundations/Plates";
        // Keep in sync with Foundation.shader (12 bits above the kind and facing bits)
        public const int MaxStyles = 4095;
        private const float PixelsPerTile = 256f;
        // A face reaches into the tile below its floor edge, never further
        private const float MaxDepthTiles = 0.95f;

        private readonly Dictionary<string, int> indexById = new();
        // Two entries per style: atlas UV rect, then (tiles across, depth in tiles, height px).
        // Style 0 (no foundation) is never drawn.
        private readonly List<Vector4> styles = new() { Vector4.zero, Vector4.zero };
        private Texture2D atlas;
        private ComputeBuffer buffer;
        private bool isDirty = true;

        public int IndexOf(string graphicId) {
            if (string.IsNullOrEmpty(graphicId)) {
                graphicId = FallbackId;
            }
            if (indexById.TryGetValue(graphicId, out var index)) {
                return index;
            }
            // The fallback goes first: its atlas is the one every other strip must share
            var fallback = graphicId == FallbackId ? None : IndexOf(FallbackId);
            index = Register(graphicId);
            if (index == None) {
                index = fallback;
            }
            indexById[graphicId] = index;
            return index;
        }

        private int Register(string graphicId) {
            if (!RenderingService.Sprites.TryFind(graphicId, out var sd)) {
                D.Err("Foundation graphic not found: {0}, drawing {1} instead",
                    graphicId, FallbackId);
                return None;
            }
            if (atlas == null) {
                atlas = sd.Atlas;
            } else if (sd.Atlas != atlas) {
                D.Err("Foundation graphic {0} is in atlas {1}, not with the other foundations, drawing {2} instead. Put it under Structure/Foundations/",
                    graphicId, sd.AtlasId, FallbackId);
                return None;
            }
            var index = styles.Count / 2;
            if (index > MaxStyles) {
                D.Err("Too many foundation graphics, drawing {0} instead of {1}",
                    FallbackId, graphicId);
                return None;
            }
            var uv = sd.UV;
            // The atlas is rescaled like the others: Res.LoadTexture divides a texture by two
            // per texture quality step, so the source size is the loaded size times that
            var pxPerTile = PixelsPerTile
                / (1 << Mathf.Max(0, CachedPlayerPrefs.GetInt(Res.TextureSizePref, 0)));
            styles.Add(new Vector4(uv.xMin, uv.yMin, uv.width, uv.height));
            styles.Add(new Vector4(sd.Width / pxPerTile,
                Mathf.Min(sd.Height / pxPerTile, MaxDepthTiles), sd.Height, 0));
            isDirty = true;
            return index;
        }

        public void Flush() {
            if (!isDirty) { return; }
            isDirty = false;
            if (buffer == null || buffer.count < styles.Count) {
                Release();
                buffer = new ComputeBuffer(styles.Count, sizeof(float) * 4);
                buffer.name = "FoundationStyles";
                The.Alloc.Register(buffer);
            }
            buffer.SetData(styles);
            Shader.SetGlobalBuffer(ShaderVars._FoundationStyles, buffer);
            if (atlas != null) {
                Shader.SetGlobalTexture(ShaderVars._FoundationTex, atlas);
            }
        }

        public void Dispose() {
            Shader.SetGlobalBuffer(ShaderVars._FoundationStyles, (ComputeBuffer) null);
            Release();
        }

        private void Release() {
            if (buffer == null) { return; }
            buffer.Release();
            The.Alloc.Unregister(buffer);
            buffer = null;
        }
    }
}
