using System;
using Game.Constants;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.Firex {
    // When each tile's firefoam starts to show, for Firefoam.shader: one float per tile, in
    // seconds of the ambient clock (AmbientMotion), so a spray blooms outward from where it
    // popped and holds still while the game is paused. Never is a tile without foam, LongAgo
    // foam that is simply there (loaded, or moved with its section). Owned by DirtSys and sent
    // to the GPU once a frame when it changed, like FloorKindBuffer.
    public sealed class FirefoamBuffer {
        public const float Never = 1e9f;
        public const float LongAgo = -1e9f;

        private readonly float[] data;
        private readonly ComputeBuffer buffer;
        private bool isDirty;

        public FirefoamBuffer(int size) {
            data = new float[size];
            Array.Fill(data, Never);
            buffer = new ComputeBuffer(size, sizeof(float));
            buffer.name = "FirefoamBuffer";
            The.Alloc.Register(buffer);
            buffer.SetData(data);
            Shader.SetGlobalBuffer(ShaderVars._Firefoam, buffer);
        }

        public void Show(int pos, float revealAt) {
            data[pos] = revealAt;
            isDirty = true;
        }

        public void Hide(int pos) {
            data[pos] = Never;
            isDirty = true;
        }

        public void HideAll() {
            Array.Fill(data, Never);
            isDirty = true;
        }

        public void Flush() {
            if (!isDirty) { return; }
            isDirty = false;
            buffer.SetData(data);
        }

        public void Dispose() {
            Shader.SetGlobalBuffer(ShaderVars._Firefoam, (ComputeBuffer) null);
            buffer.Release();
            The.Alloc.Unregister(buffer);
        }
    }
}
