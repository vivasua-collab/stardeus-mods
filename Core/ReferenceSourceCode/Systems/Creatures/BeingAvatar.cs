using System.Collections;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Utils;
using Game.CodeGen;
using KL.Utils;
using UnityEngine;
using System;
using System.Linq;
using UnityEngine.UI;

namespace Game.Systems.Creatures {
    public sealed class BeingAvatar {
        private const int size = 256;
        public RenderTexture RT;
        public Texture2D Tex;
        private Sprite sprite;
        private static Material mat;
        private static Color32[] clearPixels;
        private BeingData pendingData;

        public void Load(Action<Sprite> onLoaded) {
            // The sprite exists from the first Build and its texture is redrawn in place, so it
            // is never null, even while the draw is deferred. Holders that capture it once
            // (notifications) would otherwise keep a null sprite and render white.
            onLoaded(sprite);
        }
        public void Load(Image image) {
            Load((s) => image.sprite = s);
        }

        private BeingAvatar() {
            if (mat == null) {
                mat = new Material(Shader.Find("BeingAvatar"));
            }
        }
        public static BeingAvatar CreateFor(BeingData data) {
            var avatar = new BeingAvatar();
            avatar.Build(data);
            return avatar;
        }

        public void Rebuild(BeingData data) {
            Build(data);
        }

        public void Build(BeingData data) {
            // Only the draw needs a rendering-safe frame; the texture and sprite can exist
            // right away, so callers never get a null sprite.
            EnsureTexture();
            // If we're in a GUI/rendering context, defer the build to next frame
            if (Event.current != null) {
                if (pendingData == null) {
                    pendingData = data;
                    Coroutines.StartDirect(DeferredBuild());
                }
                return;
            }
            BuildInternal(data);
        }

        private void EnsureTexture() {
            if (Tex != null) { return; }
            Tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            The.Alloc.Register(Tex);
            // A new texture is uninitialized; start transparent until the first draw.
            clearPixels ??= new Color32[size * size];
            Tex.SetPixels32(clearPixels);
            Tex.Apply();
            sprite ??= Sprite.Create(Tex, new Rect(0, 0, size, size), Consts.V2Half);
        }

        private IEnumerator DeferredBuild() {
            // Wait one frame (necessary!)
            var s = A.S;
            yield return null;
            if (!GameState.IsCurrent(s)) { yield break;}
            if (pendingData != null) {
                var data = pendingData;
                pendingData = null;
                BuildInternal(data);
            }
        }

        private void BuildInternal(BeingData data) {
            var isDead = data.HasStateFlag(BeingState.Dead);
            mat.SetInt(ShaderVars._IsGrayscale, isDead ? 1 : 0);
            var looks = data.Looks;
            RT = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.Default, 0);
            int numSprites = 0;
            Graphics.SetRenderTarget(RT);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, 1, 1, 0);
            GL.Clear(true, true, new Color32(0, 0, 0, 0));
            var looksDef = data.Species.LooksDef;
            var facing = Facing.ConvertRotation(looksDef.PreviewRot[0]);
            foreach (var lookPart in looks.Parts.OrderBy(p => looksDef.SortOrderFor(p, facing))) {
                if (lookPart.SpriteId == null) { continue; }
                if (lookPart.Type == LookPartTypeDef.Hat) {
                    continue;
                }
                var spr = lookPart.SpriteId;
                bool ignoreColor = false;
                if (lookPart.IsFace) {
                    data.Species.LooksDef.ResolveFace(data, lookPart, true, true, ref spr,
                        ref ignoreColor);
                }
                spr = string.Intern($"{spr}_{looksDef.PreviewRot}");

                if (Images.TrySprite(spr, out var sprite)) {
                    Stamp(sprite, lookPart, looksDef, ignoreColor);
                    numSprites++;
                } else {
                    D.Err("Sprite does not exist: {0}", spr);
                }
            }
            if (numSprites == 0) {
                D.Err("Could not find the sprite for being looks: {0}", D.DumpInstance(looks));
            }
            Tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            Tex.Apply();
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(RT);
        }


        private void Stamp(Sprite sprite, LookPart part, SpeciesLooksDef def,
                bool ignoreColor) {
            var texW = sprite.texture.width;
            var texH = sprite.texture.height;
            var r = sprite.rect;
            var rect = new Rect(
                r.x / texW,
                r.y / texH,
                r.width / texW,
                r.width / texH
            );

            var finalScale = def.PreviewScale * part.Scale;
            var scaleOffset = (1f - finalScale) * 0.5f;

            var partOffX = part.Offset.x;
            var partOffY = part.Offset.y;

            // Weapon has an exception where its offset is additionally modified
            // by another offset
            if (part.Type == LookPartTypeDef.Weapon) {
                partOffX += def.PreviewOffsetWeapon.x * finalScale;
                partOffY += def.PreviewOffsetWeapon.y * finalScale;
            } else if (part.IsFace) {
                partOffX /= scaleOffset;
                partOffY /= scaleOffset;
            }

            var finalOffX = scaleOffset + def.PreviewOffset.x + partOffX;

            var screenRect = new Rect(
                finalOffX,
                scaleOffset - def.PreviewOffset.y - partOffY,
                finalScale,
                finalScale);

            if (ignoreColor) {
                mat.SetColor(ShaderVars._Color, Consts.ColorWhite);
            } else {
                var color = part.Color;
                if (part.Type == LookPartTypeDef.Body) {
                    // show sleeping bodies
                    color.a = 1f;
                }
                mat.SetColor(ShaderVars._Color, color);
            }

            Graphics.DrawTexture(screenRect, sprite.texture, rect,
                0, 0, 0, 0, mat);
        }

        public void Cleanup() {
            pendingData = null;
            if (Tex != null) {
                The.Alloc.UnregisterAndDispose(Tex);
            }
            GameObject.Destroy(sprite);
            sprite = null;
            Tex = null;
        }
    }
}
