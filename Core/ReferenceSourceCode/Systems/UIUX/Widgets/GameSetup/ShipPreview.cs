using System.Collections;
using Game.Constants;
using Game.Modifiers;
using Game.Systems.Story;
using Game.Systems.UIUX;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Scenarios {
    public sealed class ShipPreview {
        private int lastSeed;
        private int lastShipW;
        private float lastShipSize;
        private bool isPreviewGenRunning;
        private bool isPreviewGenNeeded;
        public bool IsBusy => isPreviewGenNeeded || isPreviewGenRunning || previewCrt != null;
        private readonly GameSetupData data;
        private Texture2D shipPreviewTex;
        public WRawImage wPreview;
        private CoroutineItem previewCrt;
        public bool HasPreview;
        public ShipPreview(GameSetupData data, WRawImage wRawImage) {
            this.data = data;
            wPreview = wRawImage;
        }

        public void Cleanup() {
            D.Log("DOIGN CLEANUP");
            // A preview still generating would prime the static grid again after the
            // setup handed it back, and write into the disposed texture
            if (previewCrt != null) {
                Coroutines.Cancel(previewCrt);
                previewCrt = null;
            }
            isPreviewGenRunning = false;
            isPreviewGenNeeded = false;
            if (shipPreviewTex != null) {
                The.Alloc.UnregisterAndDispose(shipPreviewTex);
                shipPreviewTex = null;
            }
        }

        public void Rebuild() {
            isPreviewGenNeeded = true;
        }

        public void OnUpdate() {
            if (!isPreviewGenNeeded) { return; }
            if (!isPreviewGenRunning) {
                previewCrt = Coroutines.Start(UpdateShipPreview(), this,
                    "UpdateShipPreview", true, false);
            } else {
                if (previewCrt == null || previewCrt.IsCancelled || previewCrt.IsFinished
                        || previewCrt.RealtimeDuration > 5f) {
                    D.Warn("Preview coroutine got stale {0}", previewCrt);
                    Coroutines.Cancel(previewCrt);
                    previewCrt = null;
                    isPreviewGenRunning = false;
                    isPreviewGenNeeded = true;
                }
            }
        }

        private IEnumerator UpdateShipPreview() {
            if (isPreviewGenRunning) {
                D.Err("Preview gen running more than once!");
                yield break;
            }
            isPreviewGenRunning = true;
            isPreviewGenNeeded = false;
            // 2. figure out the map size
            var mapW = ModifierMapSize.Current;
            var seed = data.ShipSeed;
            if (seed == 0) {
                // wPreview.SetSprite(IconId.CWarning);
                yield break;
            }
            var shipSize = ModifierShipSize.Current;
            if (lastShipW == mapW && lastSeed == seed
                    && lastShipSize == shipSize) {
                yield break;
            }
            lastShipW = mapW;
            lastSeed = seed;
            lastShipSize = shipSize;
            // 3. check if shipPreviewRT is not null and of the right size
            if (shipPreviewTex?.width != mapW) {
                if (shipPreviewTex != null) {
                    The.Alloc.UnregisterAndDispose(shipPreviewTex);
                }
                // 4. if not, recreate shipPrevieRT (release the old one if needed)
                shipPreviewTex = new Texture2D(mapW, mapW, TextureFormat.ARGB32,
                    0, true);
                shipPreviewTex.filterMode = FilterMode.Point;
                The.Alloc.Register(shipPreviewTex);
            }
            // 5. call some method on scenario to generate a preview on the RT
            yield return data.Scenario.GeneratePreview(shipPreviewTex, data);
            if (wPreview != null) {
                // 6. set the shipPrevieRT on rawImage widget
                TextureUtils.EnlargeContent(shipPreviewTex, false);
                wPreview.SetTexture(shipPreviewTex);
                HasPreview = true;
            }
            // 7. profit
            isPreviewGenRunning = false;
        }
    }
}
