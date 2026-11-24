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

namespace Game.Visuals {
    public sealed class VFXLine {
        private VisualEffect effect;
        private bool isPlaying;
        private bool isDestroyed;

        public void Stop() {
            if (!isPlaying) { return; }
            isPlaying = false;
            effect.Stop();
            effect.gameObject.SetActiveFast(false);
        }

        public void Update(float4 fromTo, float4 color, bool checkIfPlaying = true) {
            #if !PRODUCTION_BUILD
            if (color.w < 0.0001f) {
                D.Err("Trying to update VFXLine with color that has 0 alpha! {0}/{1}",
                    fromTo, color);
            }
            #endif
            if (isDestroyed) { return; }
            if (checkIfPlaying && !isPlaying) {
                Show(fromTo, color);
                return;
            }
            effect.SetVector4(ShaderVars.FromTo, fromTo);
            effect.SetVector4(ShaderVars.Color, color);
        }

        private void Show(float4 fromTo, float4 color) {
            if (!isPlaying) {
                effect.gameObject.SetActiveFast(true);
            }
            effect.Reinit();
            Update(fromTo, color, false);
            if (!isPlaying) {
                effect.Play();
                isPlaying = true;
            }
        }

        public VFXLine(string vfxName, Vector4 bounds) {
            var prefab = ResourceLibrary.FindPrefab<VisualEffect>("VFXHolder");
            effect = PrefabPool.Spawn(prefab, Consts.V3Zero, Consts.QId,
                ResourceLibrary.Root);
            effect.gameObject.SetActiveFast(false);
            effect.gameObject.name = $"VFXHolder_{vfxName}";
            effect.visualEffectAsset = Resources.Load<VisualEffectAsset>(
                $"VFX/{vfxName}");
            effect.SetVector4(ShaderVars.Viewport, bounds);
        }

        public void Destroy() {
            if (effect != null) {
                PrefabPool.Despawn(effect.gameObject, onExit: true);
                isDestroyed = true;
                effect = null;
            }
        }
    }
}
