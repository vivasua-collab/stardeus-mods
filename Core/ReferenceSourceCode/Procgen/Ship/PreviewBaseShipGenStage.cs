using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Commands;
using Game.Constants;
using Game.Data;
using KL.Collections;
using KL.Grid;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Procgen {
    public abstract class PreviewBaseShipGenStage : BaseShipGenStage, IShipPreviewGenStage {
        public void GeneratePreview(Texture2D tex) {
            isPreview = true;
            previewTex = tex;
            Apply();
        }
        public IEnumerator GeneratePreviewAsync(Texture2D tex) {
            isPreview = true;
            previewTex = tex;
            yield return ApplyAsync();
        }
    }
}
