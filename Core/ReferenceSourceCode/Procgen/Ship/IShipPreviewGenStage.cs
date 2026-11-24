using KL.Grid;
using Game.Data;
using UnityEngine;
using System;
using System.Collections;

namespace Game.Procgen {
    public interface IShipPreviewGenStage : IShipGenStage {
        void GeneratePreview(Texture2D tex);
        IEnumerator GeneratePreviewAsync(Texture2D tex);
    }
}
