#if UNITY_EDITOR
#define ALWAYS_REBUILD_PREVIEW
#endif
using System;
using System.Collections;
using System.Linq;
using Game.Constants;
using Game.Data;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Creatures {
    public sealed class BeingSurgeryPreview {
        private const int Size = 400;
        private const int Padding = 100;
        private const int TotalSize = Size + Padding * 2;
        private static Material mat;

        // Face close-up — built with avatar preview settings, used for Head tab
        private Texture2D headTex;
        // Full body — built with reduced scale + Y shift, used for Body/Organs tabs
        private Texture2D bodyTex;

        public Texture2D HeadTex => headTex;
        public Texture2D BodyTex => bodyTex;

        private int lastHeadHash;
        private int lastBodyHash;
        private float lastBodyScale;
        private float lastBodyYShift;
        private bool headBuilt;
        private bool bodyBuilt;
        private bool headBuildPending;
        private bool bodyBuildPending;
        private Action headOnBuilt;
        private Action bodyOnBuilt;

        public BeingSurgeryPreview() {
            if (mat == null) {
                mat = new Material(Shader.Find("BeingAvatar"));
            }
        }

        public void EnsureHeadBuilt(BeingData data, Action onBuilt = null) {
            var hash = data.Looks.GetHashCode();
            #if ALWAYS_REBUILD_PREVIEW
            lastHeadHash = -1;
            #endif
            if (headBuilt && hash == lastHeadHash) {
                onBuilt?.Invoke();
                return;
            }
            if (Event.current != null) {
                headOnBuilt += onBuilt;
                if (!headBuildPending) {
                    headBuildPending = true;
                    Coroutines.StartDirect(DeferredBuild(data, false));
                }
                return;
            }
            Build(data, false, ref headTex);
            lastHeadHash = hash;
            headBuilt = true;
            onBuilt?.Invoke();
        }

        public void EnsureBodyBuilt(BeingData data, Action onBuilt = null) {
            var looks = data.Looks;
            var looksDef = data.Species.LooksDef;
            var hash = looks.GetHashCode();
            #if ALWAYS_REBUILD_PREVIEW
            lastBodyHash = -1;
            #endif
            if (bodyBuilt && hash == lastBodyHash
                    && lastBodyScale == looksDef.BodyPreviewScaleMul
                    && lastBodyYShift == looksDef.BodyPreviewYShift) {
                onBuilt?.Invoke();
                return;
            }
            if (Event.current != null) {
                bodyOnBuilt += onBuilt;
                if (!bodyBuildPending) {
                    bodyBuildPending = true;
                    Coroutines.StartDirect(DeferredBuild(data, true));
                }
                return;
            }
            Build(data, true, ref bodyTex);
            lastBodyHash = hash;
            lastBodyScale = looksDef.BodyPreviewScaleMul;
            lastBodyYShift = looksDef.BodyPreviewYShift;
            bodyBuilt = true;
            onBuilt?.Invoke();
        }

        /// <summary>
        /// Pushes a re-render of whichever texture(s) were already built. Unlike EnsureXBuilt,
        /// this does not depend on data.Looks.GetHashCode() - it exists because the resolved
        /// face sprite (eye removed/robotic/animal overrides, see SpeciesLooksDef.ResolveFace)
        /// is driven by body slot state that the Looks hash cannot see. Called by
        /// BeingLooksComp.Rebuild next to BeingAvatar.Rebuild whenever a body part changes.
        /// </summary>
        public void Rebuild(BeingData data) {
            if (headBuilt) {
                headBuilt = false;
                EnsureHeadBuilt(data);
            }
            if (bodyBuilt) {
                bodyBuilt = false;
                EnsureBodyBuilt(data);
            }
        }

        private IEnumerator DeferredBuild(BeingData data, bool isBodyView) {
            var s = A.S;
            yield return null;
            if (!GameState.IsCurrent(s)) { yield break; }
            if (isBodyView) {
                var looks = data.Looks;
                var looksDef = data.Species.LooksDef;
                Build(data, true, ref bodyTex);
                lastBodyHash = looks.GetHashCode();
                lastBodyScale = looksDef.BodyPreviewScaleMul;
                lastBodyYShift = looksDef.BodyPreviewYShift;
                bodyBuilt = true;
                bodyBuildPending = false;
                var cb = bodyOnBuilt;
                bodyOnBuilt = null;
                cb?.Invoke();
            } else {
                Build(data, false, ref headTex);
                lastHeadHash = data.Looks.GetHashCode();
                headBuilt = true;
                headBuildPending = false;
                var cb = headOnBuilt;
                headOnBuilt = null;
                cb?.Invoke();
            }
        }

        private void Build(BeingData data, bool isBodyView, ref Texture2D tex) {
            var looks = data.Looks;
            var looksDef = data.Species.LooksDef;

            mat.SetInt(ShaderVars._IsGrayscale, 0);

            var rt = RenderTexture.GetTemporary(TotalSize, TotalSize, 0, RenderTextureFormat.Default, 0);
            Graphics.SetRenderTarget(rt);
            GL.PushMatrix();
            var pad = (float)Padding / TotalSize;
            GL.LoadPixelMatrix(-pad, 1f + pad, 1f + pad, -pad);
            GL.Clear(true, true, new Color32(0, 0, 0, 0));

            var rot = looksDef.PreviewRot;
            if (isBodyView && looksDef.BodyPreviewRot != null) {
                rot = looksDef.BodyPreviewRot;
            } else if (looksDef.HeadPreviewRot != null) {
                rot = looksDef.HeadPreviewRot;
            }
            var facing = Facing.ConvertRotation(rot[0]);

            foreach (var lookPart in looks.Parts.OrderBy(p => looksDef.SortOrderFor(p, facing))) {
                if (lookPart.SpriteId == null) { continue; }
                var t = lookPart.Type;
                if (t == LookPartTypeDef.Clothing || t == LookPartTypeDef.Weapon || t == LookPartTypeDef.Shadow
                        || t == LookPartTypeDef.Hat) {
                    continue;
                }
                var spr = lookPart.SpriteId;
                bool ignoreColor = false;
                if (lookPart.IsFace) {
                    looksDef.ResolveFace(data, lookPart, true, true, ref spr, ref ignoreColor);
                }
                spr = string.Intern($"{spr}_{rot}");
                if (Images.TrySprite(spr, out var lookSprite)) {
                    Stamp(lookSprite, lookPart, looksDef, ignoreColor, isBodyView);
                } else {
                    D.Err("Surgery preview sprite does not exist: {0}", spr);
                }
            }

            if (tex == null) {
                tex = new Texture2D(TotalSize, TotalSize, TextureFormat.RGBA32, false);
                The.Alloc.Register(tex);
            }
            tex.ReadPixels(new Rect(0, 0, TotalSize, TotalSize), 0, 0, false);
            tex.Apply();
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);
            RenderTexture.ReleaseTemporary(rt);
        }

        private static void Stamp(Sprite sprite, LookPart part, SpeciesLooksDef def,
                bool ignoreColor, bool isBodyView) {
            var texW = sprite.texture.width;
            var texH = sprite.texture.height;
            var r = sprite.rect;
            var rect = new Rect(r.x / texW, r.y / texH, r.width / texW, r.width / texH);

            const float scaleFix = (float)Size * (TotalSize + 2 * Padding) / ((float)TotalSize * TotalSize);

            var previewScale = isBodyView
                ? def.PreviewScale * def.BodyPreviewScaleMul
                : def.PreviewScale;
            var finalScale = previewScale * part.Scale * scaleFix;
            var scaleOffset = (1f - finalScale) * 0.5f;

            var bsm = isBodyView ? def.BodyPreviewScaleMul : 1f;
            var partOffX = part.Offset.x * bsm * scaleFix;
            var partOffY = part.Offset.y * bsm * scaleFix;

            if (part.IsFace) {
                partOffX /= scaleOffset;
                partOffY /= scaleOffset;
            }


            var finalOffX = scaleOffset + def.PreviewOffset.x + partOffX;
            var finalOffY = scaleOffset - def.PreviewOffset.y - partOffY
                + (isBodyView ? def.BodyPreviewYShift : 0f);
            var screenRect = new Rect(finalOffX, finalOffY, finalScale, finalScale);

            if (ignoreColor) {
                mat.SetColor(ShaderVars._Color, Consts.ColorWhite);
            } else {
                var color = part.Color;
                color.a = 1f;
                mat.SetColor(ShaderVars._Color, color);
            }

            Graphics.DrawTexture(screenRect, sprite.texture, rect, 0, 0, 0, 0, mat);
        }

        public void Cleanup() {
            DisposeTexture(ref headTex);
            DisposeTexture(ref bodyTex);
            headBuilt = false;
            bodyBuilt = false;
            headBuildPending = false;
            bodyBuildPending = false;
            headOnBuilt = null;
            bodyOnBuilt = null;
        }

        private static void DisposeTexture(ref Texture2D tex) {
            if (tex != null) {
                The.Alloc.UnregisterAndDispose(tex);
                tex = null;
            }
        }
    }
}
