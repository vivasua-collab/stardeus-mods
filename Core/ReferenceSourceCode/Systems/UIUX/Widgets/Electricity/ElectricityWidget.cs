using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Systems.Electricity;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class ElectricityWidget : BaseWidget, ICloseableWidget {
        [SerializeField] private WButtonText wPanelHeader;
        [SerializeField] private WGroup grids;
        [SerializeField] private WTabRow wTabs;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WGroup wItems;
        [SerializeField] private WRawImage wChartImage;
        [SerializeField] private WGroup Footer;

        public WGroup Grids => grids;
        public WTabRow Tabs => wTabs;
        public WGroup WItems => wItems;
        public WGroup FooterGroup => Footer;
        public WRawImage ChartImage => wChartImage;

        public Action<Lifetime> OnShow;
        public Action<Lifetime> OnTabChanged;

        private Lifetime lifetime;
        private ElectricityContext ctx;
        public ElectricityContext Ctx => ctx;
        private GameState S;

        private RenderTexture chartTex;
        private RTChart chart;
        private bool isChartTooltipActive;
        private int lastChartX = -1;
        private int lastChartY = -1;
        private Color chartBgColor;
        private Color chartGradColor;

        public Lifetime CurrentLifetime => lifetime;

        public void Initialize(GameState s, ElectricityContext ctx) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.Electricity, this);
            wPanelHeader.Load(T.ElectricityGrid, Close);
            S = s;
            this.ctx = ctx;
            wTabs.DestroyChildren();
            wItems.DestroyChildren();
            grids.DestroyChildren();
            Footer.DestroyChildren();
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
                if (gameObject.activeSelf) {
                    S.UIQuery.RebuildElectricity.Ask();
                }
            }
        }

        public override void Toggle(bool show) {
            UIShowing.Set(UIShowingFlags.Electricity, show);
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
                ctx.SelectedTab = null;
                ctx.SelectedGridIdx = -1;
                // The grid picked here filters the overlay, which may stay open
                S.Sys.Electricity.UI.SetSelectedGrid(-1, false);
            }
        }

        public void Load() {
            Cleanup();
            lifetime = ctx.MainLifetime = Lifetime.Create("Electricity");
            OnShow?.Invoke(lifetime);
        }

        public void Rebuild() {
            if (!gameObject.activeSelf) { return; }
            Load();
        }

        public void RebuildContent() {
            if (!gameObject.activeSelf || lifetime == null) { return; }
            OnTabChanged?.Invoke(lifetime);
        }

        public void ShowChart(bool show) {
            D.Warn("rendering chart: {0}", show);
            if (wChartImage != null) {
                wChartImage.gameObject.SetActiveFast(show);
            }
            if (!show) {
                ReleaseChartTexture();
            }
        }

        public void RenderElectricityChart(List<RTChartNode> data,
                Func<Vector2, Vector2, float, float, float, float, List<RTChartNode>, string> tooltipFunc,
                Func<float, string> labelY) {
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
                chart = new RTChart(wChartImage.gameObject, chartTex,
                    chartBgColor, chartGradColor);
            }

            chart.TooltipFunc = tooltipFunc;
            chart.Render(data, false, labelY);
        }

        public void ToggleChartView() {
            chart?.ToggleView();
        }

        private void Update() {
            if (chart == null || wChartImage == null || !wChartImage.gameObject.activeSelf) {
                return;
            }
            var rawImage = wChartImage.GetComponentInChildren<RawImage>();
            if (rawImage == null) { return; }

            var sp = The.Bindings.Mouse.ScreenPosition;
            var x = Mathf.RoundToInt(sp.x);
            var y = Mathf.RoundToInt(sp.y);
            if (lastChartX == x && lastChartY == y) { return; }
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
                    HideChartTooltip();
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

        public void Close() {
            S.UIQuery.ToggleElectricity.Ask(false);
        }

        public bool TryCloseWidget() {
            Toggle(false);
            return true;
        }

        public void Cleanup() {
            ShowChart(false);
            if (Footer != null) { Footer.SetActiveFast(false); }
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            ctx.MainLifetime = null;
            ReleaseChartTexture();
        }

        private void OnDisable() {
            ReleaseChartTexture();
        }
    }
}
