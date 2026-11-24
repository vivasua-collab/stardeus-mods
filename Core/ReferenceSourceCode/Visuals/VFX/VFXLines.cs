using System;
using Game.Constants;
using KL.Pool;
using KL.Utils;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem.Composites;
using UnityEngine.VFX;

namespace Game.Visuals {
    public sealed class VFXLines {
        private VisualEffect effect;
        public int MaxParticles;
        // FIXME switch to ComputeBuffer
        private Texture2D positionsTex;
        private Texture2D colorsTex;
        private NativeArray<float4> positions;
        private NativeArray<float4> colors;
        private Color color;
        private bool isMultiColor;

        private bool isPlaying;

        public void Stop() {
            if (!isPlaying) { return; }
            isPlaying = false;
            effect.SetFloat(ShaderVars.Count, 0);
            effect.Stop();
            effect.gameObject.SetActive(false);
        }

        private int maxLinesAttempted;

        private int lastCount;

        public void Update(int count, bool checkIfPlaying = true) {
            if (count == 0) {
                Stop();
                return;
            }
            if (count > MaxParticles) {
                if (count > maxLinesAttempted) {
                    maxLinesAttempted = count;
                    D.Err("Trying to show more VFXLines particles that we can handle! {0} > {1}",
                        count, MaxParticles);
                }
                count = MaxParticles;
            }
            if (checkIfPlaying && !isPlaying) {
                Show(count);
                return;
            }
            var pos = positionsTex.GetRawTextureData<float4>();
            if (isMultiColor) {
                var colors = colorsTex.GetRawTextureData<float4>();
                for (int i = 0; i < count; i++) {
                    pos[i] = positions[i];
                    colors[i] = this.colors[i];
                }
                colorsTex.Apply(false);
            } else {
                for (int i = 0; i < count; i++) {
                    pos[i] = positions[i];
                }
            }
            positionsTex.Apply(false);
            effect.SetFloat(ShaderVars.Count, count);
            if (count != lastCount) {
                lastCount = count;
                effect.Reinit();
            }
        }

        public void Show(int count) {
            if (count == 0) {
                effect.SetFloat(ShaderVars.Count, 0);
                return;
            }
            if (!isPlaying) {
                effect.gameObject.SetActive(true);
            }
            effect.Reinit();
            Update(count, false);
            // D.Log("Showing {0} particles", count);
            if (!isPlaying) {
                effect.Play();
                isPlaying = true;
            }
        }

        public VFXLines(string vfxName, int maxParticles, Vector4 bounds) :
                this(vfxName, maxParticles, Consts.ColorWhite, true, bounds) { }

        public VFXLines(string vfxName, int maxParticles, Color color, Vector4 bounds) :
                this(vfxName, maxParticles, color, false, bounds) { }

        // For single-color lines whose color comes from the UI theme, which can change mid-game
        public void SetColor(Color color) {
            this.color = color;
            effect.SetVector4(ShaderVars.Color, color);
        }

        public int AddLink(Vector2 from, Vector2 to, float4 color, int count) {
            if (MaxParticles > count) {
                positions[count] = new float4(from.x, from.y, to.x, to.y);
                colors[count] = color;
            }
            return ++count;
        }

        public int AddLink(Vector2 from, Vector2 to, int count) {
            if (MaxParticles > count) {
                if (Vectors.SqrDistance(from, to) < 0.01f) { return count; }
                positions[count] = new float4(from.x, from.y, to.x, to.y);
            }
            return ++count;
        }

        private VFXLines(string vfxName, int maxParticles, Color color,
                bool isMultiColor, Vector4 bounds) {
            this.isMultiColor = isMultiColor;

            maxParticles = MaxParticles = Math.Min(maxParticles, SystemInfo.maxTextureSize);

            positionsTex = new(maxParticles, 1, TextureFormat.RGBAFloat, false);
            positionsTex.name = "VFXLinesPositions";
            positionsTex.filterMode = FilterMode.Point;

            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            effect = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            effect.gameObject.SetActive(false);
            effect.gameObject.name = $"VFXHolder_{vfxName}";
            effect.visualEffectAsset = Resources.Load<VisualEffectAsset>(
                $"VFX/{vfxName}");

            positions = new(maxParticles, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            if (isMultiColor) {
                colorsTex = new(maxParticles, 1, TextureFormat.RGBAFloat, false);
                colorsTex.name = "VFXLinesColors";
                colorsTex.filterMode = FilterMode.Point;
                colors = new(maxParticles, Allocator.Persistent);
                effect.SetTexture(ShaderVars.ColorsTex, colorsTex);
            } else {
                this.color = color;
                effect.SetVector4(ShaderVars.Color, color);
            }

            effect.SetTexture(ShaderVars.PositionsTex, positionsTex);
            effect.SetInt(ShaderVars.MaxCount, MaxParticles);
            effect.SetVector4(ShaderVars.Viewport, bounds);
        }

        public void Destroy() {
            if (effect != null) {
                PrefabPool.Despawn(effect.gameObject, onExit: true);
                effect = null;
            }
            if (positionsTex != null) {
                GameObject.Destroy(positionsTex);
                positions.Dispose();
                if (isMultiColor) {
                    colors.Dispose();
                    GameObject.Destroy(colorsTex);
                    colorsTex = null;
                }
                positionsTex = null;
            }
        }
    }
}
