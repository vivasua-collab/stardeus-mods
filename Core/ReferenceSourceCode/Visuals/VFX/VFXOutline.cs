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
    public sealed class VFXOutline {
        private readonly VisualEffect effect;
        public float4 Position;
        private bool isPlaying;
        public bool IsPlaying => isPlaying;
        public void Stop() {
            if (!isPlaying) { return; }
            isPlaying = false;
            effect.Stop();
            effect.gameObject.SetActive(false);
            //effect.Stop();
            //effect.Reinit();
        }

        public void SetOutline(Vector2 pos, Vector2 size) {
            Position = new(pos.x, pos.y, size.x / 2f, size.y / 2f);
        }

        public void SetColor(Color color) {
            effect.SetVector4(ShaderVars.Color, color);
        }


        public void Show() {
            if (!isPlaying) {
                effect.gameObject.SetActive(true);
            }
            effect.SetVector4(ShaderVars.Position, Position);
            effect.Reinit();
            // D.Log("Showing {0} particles", count);
            if (!isPlaying) {
                effect.Play();
                isPlaying = true;
            }
        }

        public VFXOutline(string vfxName, Color color, float4 bounds) {
            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            effect = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            effect.gameObject.SetActive(false);
            effect.gameObject.name = $"VFXHolder_{vfxName}";
            effect.visualEffectAsset = Resources.Load<VisualEffectAsset>(
                $"VFX/{vfxName}");
            effect.SetVector4(ShaderVars.Color, color);
            effect.SetVector4(ShaderVars.Viewport, bounds);
        }

        public void Destroy() {
            PrefabPool.Despawn(effect.gameObject, onExit: true);
        }
    }
}
