#if UNITY_EDITOR
#define DEBUG_ANIM
#endif
using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Rendering;
using Game.Utils;
using Game.Visuals;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.TileAnim {
    public sealed class TileAnimGraphics {
        private readonly GameState S;
        /// <summary>
        /// int key = definition id hash
        /// </summary>
        private readonly Dictionary<int, SpriteData[]> headGraphics = new();
        private readonly Dictionary<TileAnimation, AnimatedSpriteSet> animSprites = new();
        public TileAnimGraphics(GameState s) {
            S = s;
        }

        public void OnTickProgress(float prog) {
            // rotate idle heads, aim at targets
            SpriteData[] sprites = null;
            int lastHash = 0;
            foreach (var animSpr in animSprites) {
                var anim = animSpr.Key;
                var idH = anim.Hash;
                if (lastHash != idH) {
                    sprites = AnimSpritesFor(anim);
                    lastHash = idH;
                }
                // TODO resolve actual rotation
                var sprite = animSpr.Value;
                var frame = (int) anim.CurrentFrame;
                if (sprites.Length > frame && frame >= 0) {
                    sprite.SetSprite(0, sprites[frame], S);
                    #if DEBUG_ANIM
                    sprite.Teleport(anim.AnimatedTile.Position + anim.PosOffset);
                    sprite.Sprites[0].Scale = anim.Size;
                    #endif
                } else {
                    D.Err("TileAnim trying to show invalid frame: {0}. Sprites: {1}. NumFrames: {2} (actual {3}). Device: {4}",
                        frame, sprites.Length, anim.NumFrames, anim.NumFramesActual, anim.AnimatedTile.Definition.Id);
                    anim.CurrentFrame = 0f;
                }
            }
        }

        private SpriteData[] AnimSpritesFor(TileAnimation anim) {
            var hash = anim.Hash;
            if (hash == 0) {
                D.Err("TileAnimation has no hash! Falling back to Tile.Definition.IdH, but it may lead to errors! {0}",
                    D.DumpInstance(anim));
                hash = anim.Hash = anim.AnimatedTile.Definition.IdH;
            }
            if (headGraphics.TryGetValue(hash, out var sprites)) {
                return sprites;
            }
            var gfxId = anim.AnimSpriteId;
            var numFrames = anim.NumFrames;
            var numFramesActual = anim.NumFramesActual;
            sprites = new SpriteData[numFramesActual];
            /// Array of 8 directions, starting at down and going clockwise
            /// D -> DDiag -> L -> LDiag -> U -> UDiag -> R -> RDiag
            int idx = 0;
            if (anim.PingPong) {
                // first frame hold
                if (anim.PingPongStillFrames > 0) {
                    var sd = GetFrameData(anim, gfxId, 0);
                    for (int i = 0; i < anim.PingPongStillFrames; i++) {
                        sprites[idx++] = sd;
                    }
                }
                // loop forward
                for (int i = 0; i < numFrames; i++) {
                    SpriteData sd = GetFrameData(anim, gfxId, i);
                    sprites[idx++] = sd;
                }
                // last frame hold
                if (anim.PingPongStillFrames > 0) {
                    var sd = GetFrameData(anim, gfxId, numFrames - 1);
                    for (int i = 0; i < anim.PingPongStillFrames; i++) {
                        sprites[idx++] = sd;
                    }
                }
                // loop backward skipping first and last frame to complete the ping pong
                for (int i = numFrames - 2; i >= 1; i--) {
                    SpriteData sd = GetFrameData(anim, gfxId, i);
                    sprites[idx++] = sd;
                }
            } else {
                for (int i = 0; i < numFrames; i++) {
                    SpriteData sd = GetFrameData(anim, gfxId, i);
                    sprites[idx++] = sd;
                }
            }
            for (int i = sprites.Length - 1; i >= 0; i--) {
                sprites[i].Scale = anim.Size;
            }
            headGraphics[hash] = sprites;
            return sprites;
        }

        private static SpriteData GetFrameData(TileAnimation anim, string gfxId, int i) {
            var sd = RenderingService.Sprites.Find($"{gfxId}_{anim.FramePrefix}{i}");
            if (anim.CopyAnimSprite) {
                sd = sd.Copy();
                sd.IsTempCopy = true;
            }

            return sd;
        }

        public void PrepareFor(TileAnimation anim) {
            var sprites = AnimSpritesFor(anim);
            anim.AnimatedTile.Graphics.SetGraphicId(anim.BaseSpriteId);
            var animPos = anim.AnimatedTile.Position;
            Vector3 pos = animPos + anim.PosOffset;
            pos += new Vector3(0f, 0f, animPos.y * -0.1f);
            var curFrameSprite = new AnimatedSpriteSet(this, 1, WorldLayer.ObjectsAbove, pos);
            curFrameSprite.RenderSorted[0] = true;
            curFrameSprite.Owner = anim.AnimatedTile;
            curFrameSprite.SetSprite(0, Rng.UFrom(sprites), S);
            curFrameSprite.SetRenderer(S, 0, Renderers.DeviceParts, anim.SpriteOrder);
            if (animSprites.TryAdd(anim, curFrameSprite)) {
                curFrameSprite.AddToRenderer(S);
            }
        }
        public void UnloadFor(TileAnimation turret) {
            if (animSprites.TryGetValue(turret, out var sprite)) {
                sprite.RemoveFromRenderer(S);
                animSprites.Remove(turret);
            }
        }

        public void ResetAnims() {
            foreach (var hs in animSprites) {
                var anim = hs.Key;
                var head = hs.Value;
                head.Teleport(anim.AnimatedTile.Position + anim.PosOffset);
                head.UpdateRenderer(G.Ticks, 0f, Time.frameCount - 2);
            }
        }
    }
}
