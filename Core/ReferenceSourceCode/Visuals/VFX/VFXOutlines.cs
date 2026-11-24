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
    public sealed class VFXOutlines {
        private readonly VisualEffect effect;
        public int MaxParticles;
        // FIXME switch to ComputeBuffer
        private Texture2D positionsTex;
        public NativeArray<float4> Positions;
        private bool isPlaying;
        private int lastCount;
        public void Stop() {
            if (!isPlaying) { return; }
            isPlaying = false;
            effect.SetFloat(ShaderVars.Count, 0);
            lastCount = 0;
            effect.Stop();
            effect.gameObject.SetActive(false);
            //effect.Stop();
            //effect.Reinit();
        }

        public int AddOutline(Vector2 pos, Vector2 size, int count) {
            if (count < MaxParticles) {
                Positions[count++] = new(pos.x, pos.y,
                    size.x / 2f, size.y / 2f);
            }
            return count;
        }

        public void Update(int count, bool checkIfPlaying = true) {
            if (count == 0) {
                effect.SetFloat(ShaderVars.Count, 0);
                return;
            }
            if (checkIfPlaying && !isPlaying) {
                Show(count);
                return;
            }
            var points = positionsTex.GetRawTextureData<float4>();
            for (int i = 0; i < count; i++) {
                points[i] = Positions[i];
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
                lastCount = count;
                Stop();
                //effect.SetFloat(ShaderVars.Count, 0);
                return;
            }
            if (count > MaxParticles) {
                D.Err("Trying to show more VFXOutlines particles that we can handle! {0} > {1}",
                    count, MaxParticles);
                count = MaxParticles;
            }
            lastCount = count;
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

        public VFXOutlines(string vfxName, int maxParticles, Color color, float4 bounds) {
            MaxParticles = maxParticles;

            positionsTex = new(maxParticles, 1, TextureFormat.RGBAFloat, false);
            positionsTex.name = "VFXOutlinesPositions";
            positionsTex.filterMode = FilterMode.Point;
            Positions = new(maxParticles, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            effect = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            effect.gameObject.SetActive(false);
            effect.gameObject.name = $"VFXHolder_{vfxName}";
            effect.visualEffectAsset = Resources.Load<VisualEffectAsset>(
                $"VFX/{vfxName}");
            effect.SetVector4(ShaderVars.Color, color);
            effect.SetTexture(ShaderVars.PositionsTex, positionsTex);
            effect.SetInt(ShaderVars.MaxCount, MaxParticles);
            effect.SetVector4(ShaderVars.Viewport, bounds);
        }

        public void Destroy() {
            if (positionsTex != null) {
                GameObject.Destroy(positionsTex);
                PrefabPool.Despawn(effect.gameObject, onExit: true);
                Positions.Dispose();
                positionsTex = null;
            }
        }
    }
}
