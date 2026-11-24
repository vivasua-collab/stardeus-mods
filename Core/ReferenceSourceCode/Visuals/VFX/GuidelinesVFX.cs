using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Tools;
using Game.UI;
using KL.Grid;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Visuals {
    public sealed class GuidelinesVFX {
        private readonly GameState S;
        private readonly VFXLines inGameGuidelines;

        private float4 bounds;

        // TODO implement GuidelinesVFX
        public GuidelinesVFX(GameState s, Vector4 bounds) {
            S = s;
            this.bounds = bounds;
            inGameGuidelines = new("Guidelines", 32,
                CameraFocus.FlashColor(), bounds);
        }

        public void Destroy() {
            inGameGuidelines.Destroy();
        }
    }
}
