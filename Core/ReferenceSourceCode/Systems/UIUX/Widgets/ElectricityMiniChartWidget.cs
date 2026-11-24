using System;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Notify;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public class ElectricityMiniChartWidget : WBase, IPointerEnterHandler, IPointerExitHandler {
        [SerializeField] private RawImage rawImage;
        [SerializeField] private WButtonIconText wBtnIconText;
        [SerializeField] private RTChartSimpleMode chartMode = RTChartSimpleMode.Fill;

        private RenderTexture chartTex;
        private int lastWidth;
        private int lastHeight;
        private bool isInitialized;
        private bool isHovering;
        private readonly StringBuilder tooltipSB = new();

        public void SetColors() {
            var t = The.UITheme;
            var colorChartBlue = t.GetColor("ChartBlue", this);
            var colorChartRed = t.GetColor("ChartRed", this);
            var colorChartWhite = t.GetColor("ChartWhite", this);
            var colorChartBg = t.GetColor("MiniChartBackground", this);
            RTChartSimple.ColorBackground = colorChartBg;
            RTChartSimple.ColorConsumption = colorChartBlue;
            RTChartSimple.ColorDeficit = colorChartRed;
            RTChartSimple.ColorMaxOutput = colorChartWhite;
        }

        public void SetOnClick(Action onClick) {
            wBtnIconText.SetOnClick(onClick);
        }

        public void UpdateFull(Situation sit) {
            // Situation grid values only cover ship computer managed grids, so
            // without one there is nothing but a dash and an empty chart to show
            var hasData = A.S.Sys.Electricity.History.Count > 1
                && (sit.GridMaxOutput > 0f || sit.GridConsumption > 0f
                    || sit.GridDeficit > 0f);
            var wasShowing = gameObject.activeSelf;
            this.SetActiveFast(hasData);
            if (!hasData) {
                if (wasShowing && isHovering) {
                    // pointer exit does not arrive when we deactivate under the cursor
                    isHovering = false;
                    The.SysSig.HideTooltip.Send();
                }
                return;
            }
            var txt = Units.ElectricityIO(sit.GridMaxOutput, sit.GridConsumption + sit.GridDeficit);
            if (sit.GridDeficit > 0f) {
                wBtnIconText.SetImage(IconId.CNoEnergy);
                txt = Texts.Red(txt);
            } else {
                wBtnIconText.SetImage(IconId.CEnergy);
            }
            if (wBtnIconText.SetText(txt) || !wasShowing) {
                // we give electricity enough space upfront, rebuilds are
                // expensive and it can change frequently. When we just became
                // visible the rect is stale, and the chart texture would be
                // sized from it
                wBtnIconText.RebuildLayout();
            }
            UpdateChart();
        }

        // In the red of the widget's deficit text (UpdateFull), so the ring reads as the
        // widget's own state. Priority and spread as the hub uses for the deficit's
        // notification type, Alert
        public void RingDeficit(bool focus) {
            var color = Texts.TextColorRed;
            int priority = NotificationType.Info - NotificationType.Alert;
            if (focus) {
                UIFocusRing.Request(Rect, color, priority);
            } else if (!UIFocusRing.IsActiveOn(Rect)) {
                // A ring spreading out of the widget while another shrinks onto it reads as
                // a glitch
                UIPulseRing.Request(Rect, color, priority, 3f);
            }
        }

        private void UpdateChart() {
            if (!isInitialized) { return; }
            if (rawImage == null) { return; }

            var rect = rawImage.rectTransform.rect;
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            // Check if size changed
            if (width != lastWidth || height != lastHeight) {
                ReleaseRenderTexture();
                lastWidth = width;
                lastHeight = height;
            }

            // Create RenderTexture if needed
            if (chartTex == null) {
                chartTex = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rawImage.texture = chartTex;
            }

            // Render the chart
            var history = A.S.Sys.Electricity.History;
            RTChartSimple.RenderElectricityMini(chartTex, history, G.Ticks, chartMode);
        }

        private void ReleaseRenderTexture() {
            if (chartTex != null) {
                rawImage.texture = null;
                RenderTexture.ReleaseTemporary(chartTex);
                chartTex = null;
            }
        }

        private void OnDisable() {
            ReleaseRenderTexture();
            lastWidth = 0;
            lastHeight = 0;
        }

        private string BuildTooltip() {
            var s = A.S;
            var sys = s.Sys.Electricity;
            tooltipSB.Clear();
            int managedCount = 0;
            int unmanagedCount = 0;
            for (int i = 0; i < sys.GridsCount; i++) {
                if (sys.TryGetGrid(i, out var g) && g.Size >= 2) {
                    if (g.HasShipComputer) { managedCount++; } else { unmanagedCount++; }
                }
            }
            if (managedCount > 0) {
                tooltipSB.Append(Texts.SubTitle(T.GridsManaged));
                for (int i = 0; i < sys.GridsCount; i++) {
                    if (sys.TryGetGrid(i, out var g) && g.Size >= 2 && g.HasShipComputer) {
                        var state = Units.ElectricityIO(g.MaxOutputWithoutBat, g.Consumption);
                        if (g.Deficit > 0f) { state = Texts.Red(state); }
                        tooltipSB.AppendLine(T.EnergyGridNr_V2.F(i, state));
                    }
                }
            }
            if (unmanagedCount > 0) {
                if (tooltipSB.Length > 0) { tooltipSB.Append("\n"); }
                tooltipSB.Append(Texts.SubTitle(T.GridsUnmanaged));
                for (int i = 0; i < sys.GridsCount; i++) {
                    if (sys.TryGetGrid(i, out var g) && g.Size >= 2 && !g.HasShipComputer) {
                        tooltipSB.AppendLine(T.EnergyGridNr_V2.F(i, T.Unknown));
                    }
                }
            }
            if (sys.TryGetGrid(0, out var grid0) && grid0.Size >= 2) {
                if (tooltipSB.Length > 0) { tooltipSB.Append("\n"); }
                tooltipSB.Append(Texts.SubTitle(T.EnergyOverview));
                tooltipSB.Append(T.EnergyCurDemand);
                tooltipSB.Append(": ");
                tooltipSB.Append(Units.Electricity(grid0.Consumption + grid0.Deficit));
                tooltipSB.Append("\n");
                tooltipSB.Append(T.EnergyPeakDemand);
                tooltipSB.Append(": ");
                tooltipSB.Append(Units.Electricity(grid0.Consumption));
                tooltipSB.Append("\n");
                tooltipSB.Append(T.EnergyMaxOutput);
                tooltipSB.Append(": ");
                tooltipSB.Append(Units.Electricity(grid0.MaxOutputWithoutBat));
                tooltipSB.Append("\n");
                tooltipSB.Append(T.EnergyRenewable);
                tooltipSB.Append(": ");
                tooltipSB.Append(Units.Electricity(grid0.OutputRenewable));
                tooltipSB.Append("\n");
                tooltipSB.Append(T.EnergyCurDeficit);
                tooltipSB.Append(": ");
                tooltipSB.Append(Units.Electricity(grid0.Deficit));
                if (grid0.MaxOutputBat > 0f) {
                    tooltipSB.Append("\n");
                    tooltipSB.Append(T.EnergyMaxBatOutput);
                    tooltipSB.Append(": ");
                    tooltipSB.Append(Units.Electricity(grid0.MaxOutputBat));
                    tooltipSB.Append("\n");
                    tooltipSB.Append(T.EnergyBatCharge);
                    tooltipSB.Append(": ");
                    tooltipSB.Append(Units.Electricity(grid0.BatCharge / grid0.BatCapacity * grid0.MaxOutputBat));
                }
            }
            return tooltipSB.ToString();
        }

        public void Setup(GameState s) {
            var show = The.Prefs.GetBool(
                Pref.GShowElectricityMiniChart,
                PrefH.GShowElectricityMiniChart, true);
            if (isInitialized && !show) {
                OnDisable();
            }
            isInitialized = show;
            rawImage.SetActiveFast(show);
            UpdateFull(s.Situation);
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (G.IsClickDrag) { return; }
            isHovering = true;
            The.SysSig.ShowDynamicTooltip.Send(BuildTooltip, true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            isHovering = false;
            The.SysSig.HideTooltip.Send();
        }
    }
}
