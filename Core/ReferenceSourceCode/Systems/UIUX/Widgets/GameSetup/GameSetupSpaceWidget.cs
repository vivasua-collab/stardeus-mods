using System;
using Game.Constants;
using Game.Input;
using Game.Procgen;
using Game.Systems.Space;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class GameSetupSpaceWidget : BaseWidget {

        [SerializeField] private WText wTxtUniverse;
        [SerializeField] private WInput wInputName;
        [SerializeField] private WButtonIcon btnRand;
        [SerializeField] private WSliderWithLabel sliderDifficulty;
        [SerializeField] private WSliderWithLabel sliderUniverseSize;
        [SerializeField] private UniversePreviewWidget universePreview;

        // private SpaceMapViz spaceMapViz;
        private GenSpace generator;
        private GameSetupData data;
        private SpaceRegion selectedRegion;
        private string wantedUniverseName;
        private float updateTimeout;
        private bool paramsChanged;
        private GenSpaceParams genParams;
        private WText txtFooterMessage;


        public void Initialize(WText txtFooterMessage) {
            this.txtFooterMessage = txtFooterMessage;
            btnRand.Load(IconId.CDice, RandomizeUniverse);
            wTxtUniverse.Load(T.UniverseSeed);
            sliderDifficulty.Label.Load(T.ModifierDifficulty);
            sliderDifficulty.Slider.Load(0f, OnChangeDifficulty, -0.5f, +0.75f);
            sliderDifficulty.Slider.SetValueTextFunc((f) => {
                f = Maths.ClampSnap(f, -0.5f, +0.75f, 0.05f);
                if (Mathf.Approximately(f, 0f)) {
                    return T.AdjDefault;
                }
                return Units.PercentageSigned(f);
            });
            sliderUniverseSize.Label.Load(T.Size);
            sliderUniverseSize.Slider.Load(1f, OnChangeUniverseSize, 0.75f, 3f);
            sliderUniverseSize.Slider.SetValueTextFunc((f) => {
                f = Maths.ClampSnap(f, 0.75f, 3f, 0.25f);
                return $"{f:0.##}x";
            });
            universePreview.OnClickRegion = SelectRegion;
            Toggle(false);
        }

        private void OnChangeUniverseSize(float size) {
            size = Maths.ClampSnap(size, 0.75f, 3f, 0.25f);
            genParams.MapSizeMul = Mathf.Sqrt(size);
            updateTimeout = 0.5f;
            paramsChanged = true;
        }

        private void OnChangeDifficulty(float shift) {
            genParams.DifficultyShift = shift;
            updateTimeout = 0.5f;
            paramsChanged = true;
        }

        private void RandomizeUniverse() {
            wantedUniverseName = GenUtils.NewGenericName(A.S);
            wInputName.SetValue(wantedUniverseName, false);
            SetUniverseName(wantedUniverseName);
        }

        public void BeginSetup(GameSetupData data) {
            this.data = data;
            wInputName.Load(T.EnterName, data.UniverseName, OnNameChange);
            wantedUniverseName = data.UniverseName;
            RebuildMap();
            Coroutines.OnNextFrame(() => {
                universePreview.FocusOnSelectedSO(data.StartingSpaceRegion.SO);
            });
        }

        public void EveryFrame() {
            universePreview.UpdateWidget();
            if (paramsChanged || wantedUniverseName != data.UniverseName) {
                updateTimeout -= GameTimer.unscaledDeltaTime;
                if (updateTimeout <= 0f) {
                    // still click dragging
                    if (paramsChanged && The.Bindings.IsPressed(ActionType.Click)) {
                        return;
                    }
                    SetUniverseName(wantedUniverseName);
                    paramsChanged = false;
                }
            }
        }

        private void SetUniverseName(string name) {
            data.SetUniverseName(name);
            if (string.IsNullOrWhiteSpace(name)) {
                universePreview.Cleanup();
            } else {
                RebuildMap();
            }
        }
        private void RebuildMap() {
            var seed = data.UniverseSeed;
            D.Warn("REBUILDING MAP! Seed: {0}", seed);
            if (generator != null) {
                generator.Cleanup();
            }
            generator = new GenSpace(A.S, seed);
            var map = generator.Generate(genParams);
            universePreview.Load(map, data.Scenario);
            if (selectedRegion != null) {
                selectedRegion = null;
            }
            universePreview.SelectRandomRegion(Rng.Unseeded);
            txtFooterMessage.SetText(map.StatsText);
        }

        private void SelectRegion(SpaceRegion region) {
            data.StartingSpaceRegion = region;
        }

        private void OnNameChange(string name) {
            wantedUniverseName = name;
            updateTimeout = 0.5f;
        }

        public void Show(Lifetime parentLife, GameSetupData data) {
            gameObject.SetActiveFast(true);
            UIShowing.Set(UIShowingFlags.ScrollableWidget, true);
            // The crew page hid it, and the same text set again does not show it
            txtFooterMessage.SetActiveFast(true);
            if (this.data != data) {
                BeginSetup(data);
            } else {
                if (data.StartingSpaceRegion?.SO != null) {
                    D.Warn("reselecting region: {0}", data.StartingSpaceRegion);
                    universePreview.SelectRegion(data.StartingSpaceRegion);
                } else {
                    universePreview.SelectRandomRegion(Rng.Unseeded);
                }
            }
            data.SetupPhase = GameSetupData.Phase.Space;
        }

        public void Cleanup() {
            data = null;
            paramsChanged = false;
            updateTimeout = 0f;
            wantedUniverseName = null;
            genParams = default;
        }

        public void Close() {
            UIShowing.Set(UIShowingFlags.ScrollableWidget, false);
            Toggle(false);
        }

        public bool TryNext() {
            if (string.IsNullOrWhiteSpace(wInputName.Text)) {
                var msg = string.Format("{0} {1}: {2}",
                    GlyphMap.SpriteWarning, T.Missing, T.UniverseSeed);
                UISounds.PlayActionDenied();
                WPopup.Create(true, IconId.CStop, T.OperationForbidden, null, msg)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
                return false;
            }
            return true;
        }
    }
}
