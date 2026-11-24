using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class SituationWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WRawImage wChartImage;

        public Action<Lifetime> OnShow;
        private Lifetime lifetime;
        private GameState S;

        private RenderTexture chartTex;
        private RTChart chart;
        private bool isChartTooltipActive;
        private int lastChartX = -1;
        private int lastChartY = -1;
        private Color chartBgColor;
        private Color chartGradColor;

        public WGroup WItems => wItems;

        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Situation, this);
            S = s;
            wPanelHeader.Load(T.Situation, Close);
            wItems.DestroyChildren();
            SetColors();
        }

        private void SetColors() {
            var t = The.UITheme;
            chartBgColor = t.GetColor("ChartBackground", this);
            chartGradColor = t.GetColor("ChartGradient", this);
        }

        public void OnThemeReloaded() {
            SetColors();
            if (chart != null) {
                ReleaseChartTexture();
                if (UIShowing.Situation) {
                    Load();
                }
            }
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Situation, show);
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        public void Load() {
            Cleanup();
            lifetime = Lifetime.Create("Situation");
            OnShow?.Invoke(lifetime);
        }

        public void ShowChart(bool show) {
            if (wChartImage != null) {
                wChartImage.gameObject.SetActiveFast(show);
            }
            if (!show) {
                ReleaseChartTexture();
            }
        }

        public void RenderSituationChart(List<RTChartNode> data,
                Func<Vector2, Vector2, float, float, float, float, List<RTChartNode>, string> tooltipFunc) {
            if (wChartImage == null) { return; }
            var rawImage = wChartImage.GetComponentInChildren<RawImage>();
            if (rawImage == null) { return; }

            var rect = rawImage.rectTransform.rect;
            int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            if (width <= 1 || height <= 1) { return; }

            if (chartTex == null || chartTex.width != width || chartTex.height != height) {
                ReleaseChartTexture();
                chartTex = RenderTexture.GetTemporary(width, height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rawImage.texture = chartTex;
                chart = new RTChart(wChartImage.gameObject, chartTex, chartBgColor, chartGradColor);
            }

            chart.SetXDays(true);
            chart.TooltipFunc = tooltipFunc;
            chart.Render(data, false);
        }

        private void Update() {
            if (chart == null || wChartImage == null || !wChartImage.gameObject.activeSelf) { return; }

            var sp = The.Bindings.Mouse.ScreenPosition;
            var x = Mathf.RoundToInt(sp.x);
            var y = Mathf.RoundToInt(sp.y);
            if (lastChartX == x && lastChartY == y) { return; }
            // only once the mouse moved, not on every frame
            var rawImage = wChartImage.GetComponentInChildren<RawImage>();
            if (rawImage == null) { return; }
            lastChartX = x;
            lastChartY = y;

            var firstUnderCursor = The.Bindings.Mouse.FirstUnderCursor;
            var isHovering = UIUtils.IsChildOf(wChartImage.transform,
                firstUnderCursor != null ? firstUnderCursor.transform : null);

            if (isHovering && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rawImage.rectTransform, sp, null, out var point)) {
                var rect = rawImage.rectTransform.rect;
                if (!rect.Contains(point)) {
                    if (isChartTooltipActive) { HideChartTooltip(); }
                    return;
                }
                isChartTooltipActive = true;
                point = new Vector2(point.x - rect.xMin, point.y - rect.yMin);
                var tt = chart.TooltipAt(point);
                if (tt != null) {
                    The.SysSig.ShowTooltip.Send(tt, true);
                } else {
                    // the last position stays: HideChartTooltip resets it, which made the
                    // next frame redo all of this and send HideTooltip again, every frame
                    isChartTooltipActive = false;
                    The.SysSig.HideTooltip.Send();
                }
            } else if (isChartTooltipActive) {
                HideChartTooltip();
            }
        }

        private void HideChartTooltip() {
            lastChartX = -1;
            lastChartY = -1;
            isChartTooltipActive = false;
            The.SysSig.HideTooltip.Send();
        }

        private void ReleaseChartTexture() {
            chart?.Unload();
            chart = null;
            if (chartTex != null) {
                RenderTexture.ReleaseTemporary(chartTex);
                chartTex = null;
            }
            if (isChartTooltipActive) {
                HideChartTooltip();
            }
        }

        public void Cleanup() {
            ShowChart(false);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ReleaseChartTexture();
        }

        private void OnDisable() {
            ReleaseChartTexture();
        }

        private void Close() {
            Toggle(false);
        }

        public bool TryCloseWidget() {
            Close();
            return true;
        }
    }
}
