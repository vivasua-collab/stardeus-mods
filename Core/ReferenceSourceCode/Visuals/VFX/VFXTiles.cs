using System;
using Game.Constants;
using KL.Pool;
using KL.Utils;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;
using Game.Rendering;
using KL.Randomness;
using KL.Grid;
using Game.Data;
using System.Collections.Generic;
using KL.Collections;

namespace Game.Visuals {
    // FIXME rewrite with compute buffers instead of textures
    public sealed class VFXTiles {
        private VisualEffect effect;
        private readonly string vfxName;
        private bool acquiredBudget;
        private bool isSpawned;
        private bool usesColorTex;
        public int MaxParticles;
        // FIXME switch to ComputeBuffer
        private Texture2D positionsTex;
        private Texture2D colorsTex;
        public NativeArray<float4> Positions;
        public NativeArray<float4> Colors;
        private readonly Dictionary<int, int> showingPositions = new();
        private bool isPlaying;
        public bool hasChanged;
        private int lastCount;
        public VFXTiles WithParticleTexture(Texture2D tex) {
            if (!isSpawned) { return this; }
            effect.SetTexture(ShaderVars.ParticleTex, tex);
            return this;
        }

        public void Stop() {
            if (!isSpawned) { return; }
            if (!isPlaying) { return; }
            isPlaying = false;
            effect.SetFloat(ShaderVars.Count, 0);
            effect.Stop();
            effect.gameObject.SetActive(false);
            showingPositions.Clear();
            hasChanged = false;
        }

        public int AddRange(int pos, int range, float tileSize, int count) {
            if (!isSpawned) { return count; }
            var size = RenderUtils.TilePulse;
            Pos.ForEachInOuterRadius(pos, range, (pos) => {
                var newHash = Hashes.Quick(17621, pos, range * 178);
                if (!showingPositions.TryGetValue(count, out var hash) || hash != newHash) {
                    showingPositions[count] = newHash;
                    hasChanged = true;
                    var p = EntityUtils.CenterOf(pos);
                    Positions[count] = new(p.x, p.y, tileSize * size, 0);
                }
                count++;
            });
            return count;
        }

        public int AddTile(int pos, float tileSize, int count) {
            if (!isSpawned) { return count; }
            // var size = RenderUtils.TilePulse;
            var size = 1f;
            hasChanged = true;
            var p = EntityUtils.CenterOf(pos);
            if (MaxParticles > count) {
                Positions[count] = new(p.x, p.y, tileSize * size, 0);
            }
            return ++count;
        }
        public int AddPos(Vector2 p, float4 color, float tileSize, int count) {
            if (!isSpawned || !usesColorTex) { return count; }
            // var size = RenderUtils.TilePulse;
            var size = 1f;
            hasChanged = true;
            if (MaxParticles > count) {
                Positions[count] = new(p.x, p.y, tileSize * size, 0);
                Colors[count] = color;
            }
            return ++count;
        }

        public int AddTile(int pos, float4 color, float tileSize, int count) {
            if (!isSpawned || !usesColorTex) { return count; }
            hasChanged = true;
            var p = EntityUtils.CenterOf(pos);
            if (MaxParticles > count) {
                Positions[count] = new(p.x, p.y, tileSize, 0);
                Colors[count] = color;
            }
            return ++count;
        }

        public void Update(int count, bool checkIfPlaying = true) {
            if (!isSpawned) { return; }
            if (count == 0) {
                effect.SetFloat(ShaderVars.Count, 0);
                return;
            }
            if (checkIfPlaying && !isPlaying) {
                Show(count);
                return;
            }
            var points = positionsTex.GetRawTextureData<float4>();

            if (usesColorTex) {
                var colors = colorsTex.GetRawTextureData<float4>();
                for (int i = 0; i < count; i++) {
                    points[i] = Positions[i];
                    colors[i] = Colors[i];
                }
                colorsTex.Apply(false);
            } else {
                for (int i = 0; i < count; i++) {
                    points[i] = Positions[i];
                }
            }
            positionsTex.Apply(false);
            effect.SetFloat(ShaderVars.Count, count);
        }

        public void Show(int count) {
            if (!isSpawned) { return; }
            if (count == 0) {
                Stop();
                return;
            }
            if (!hasChanged && lastCount == count) { return; }
            lastCount = count;
            hasChanged = false;
            if (count > MaxParticles) {
                D.Err("Trying to show more VFXTiles particles that we can handle! {0} > {1}",
                    count, MaxParticles);
                count = MaxParticles;
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

        public VFXTiles(string vfxName, int maxParticles, Color color, Vector4 bounds)
            : this(vfxName, maxParticles, color, false, bounds) {}

        public VFXTiles(string vfxName, int maxParticles, Vector4 bounds)
            : this(vfxName, maxParticles, Consts.ColorWhite, true, bounds) {}

        private VFXTiles(string vfxName, int maxParticles, Color color, bool usesColorTex, Vector4 bounds) {
            this.vfxName = vfxName;
            this.usesColorTex = usesColorTex;
            maxParticles = MaxParticles = Math.Min(maxParticles, SystemInfo.maxTextureSize);

            if (!ParticleBudget.TryAcquire(vfxName)) {
                return;
            }
            acquiredBudget = true;

            positionsTex = new Texture2D(maxParticles, 1, TextureFormat.RGBAFloat, false);
            The.Alloc.Register(positionsTex);
            positionsTex.name = "VFXTilesPositions";
            positionsTex.filterMode = FilterMode.Point;
            Positions = new NativeArray<float4>(maxParticles, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            if (usesColorTex) {
                colorsTex = new Texture2D(maxParticles, 1, TextureFormat.RGBAFloat, false);
                The.Alloc.Register(colorsTex);
                colorsTex.name = "VFXTilesColors";
                colorsTex.filterMode = FilterMode.Point;
                Colors = new NativeArray<float4>(maxParticles, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }

            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            if (prefab == null) {
                ParticleBudget.Release(vfxName);
                acquiredBudget = false;
                return;
            }
            effect = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            effect.gameObject.SetActive(false);
            effect.gameObject.name = $"VFXHolder_{vfxName}";
            effect.visualEffectAsset = Resources.Load<VisualEffectAsset>(
                $"VFX/{vfxName}");
            effect.SetTexture(ShaderVars.PositionsTex, positionsTex);
            if (usesColorTex) {
                effect.SetTexture(ShaderVars.ColorsTex, colorsTex);
            }
            effect.SetVector4(ShaderVars.Color, color);
            effect.SetInt(ShaderVars.MaxCount, MaxParticles);
            effect.SetVector4(ShaderVars.Viewport, bounds);
            isSpawned = true;
        }

        public void Destroy() {
            isSpawned = false;
            if (positionsTex != null) {
                The.Alloc.UnregisterAndDispose(positionsTex);
                if (usesColorTex) {
                    The.Alloc.UnregisterAndDispose(colorsTex);
                    colorsTex = null;
                    Colors.Dispose();
                }
                if (effect != null) {
                    PrefabPool.Despawn(effect.gameObject, onExit: true);
                    effect = null;
                }
                Positions.Dispose();
                positionsTex = null;
            }
            if (acquiredBudget) {
                ParticleBudget.Release(vfxName);
                acquiredBudget = false;
            }
        }
    }
}
