using System.Collections.Generic;
using Game;
using Game.Constants;
using Game.Data;
using Game.Rendering;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class CodexTooltipChart {
        private readonly RawImage image;
        private RenderTexture tex;
        private Def def;

        private Color bgColor;
        private Color gridColor;
        private Color lineColor;
        private const float LineThickness = 3f;
        private const float GridThickness = 1f;

        private const float PadLeft = 38f;
        private const float PadBottom = 18f;
        private const float PadTop = 8f;
        private const float PadRight = 5f;

        private readonly List<RTChartLabel> labels = new();
        private readonly RTChartLabel labelPrefab;

        private float storedMinX, storedMaxX;
        private float storedScaleX;
        public bool HasValidData { get; private set; }

        public CodexTooltipChart(RawImage image) {
            this.image = image;
            labelPrefab = ResourceLibrary.FindPrefab<RTChartLabel>("UIChartLabel");
            SetColors();
        }

        public void SetColors() {
            var t = The.UITheme;
            bgColor = t.GetColor("ChartBackground", this);
            gridColor = t.GetColor("ChartGrid", this);
            lineColor = t.GetColor("ChartLine", this);
        }

        public void Begin(Def def) {
            this.def = def;
            EnsureTexture();
            Render();
        }

        public void Tick() {
            Render();
        }

        public void End() {
            ClearLabels();
            ReleaseTexture();
            def = null;
            HasValidData = false;
        }

        public string TooltipAt(Vector2 texelPos) {
            if (!HasValidData) { return null; }
            if (texelPos.x < PadLeft || texelPos.x > tex.width - PadRight ||
                    texelPos.y < PadBottom || texelPos.y > tex.height - PadTop) {
                return null;
            }
            return GetTooltipAtX(texelPos.x);
        }

        private string GetTooltipAtX(float texX) {
            var history = def == null ? null : A.S.Query.StockHistory.Ask(def);
            if (history == null) { return null; }

            float dataTick = (texX - PadLeft) / storedScaleX + storedMinX;
            long minTime = G.Ticks - Consts.TicksPerWeek;
            if (minTime < 0) { minTime = 0; }

            long prevKey = 0;
            int prevVal = 0;
            bool hasPrev = false;

            foreach (var entry in history) {
                if (entry.Key < minTime) {
                    prevKey = entry.Key;
                    prevVal = entry.Value;
                    hasPrev = true;
                    continue;
                }
                if (entry.Key >= dataTick) {
                    float val;
                    if (hasPrev && entry.Key > dataTick) {
                        float t = (dataTick - prevKey) / (float)(entry.Key - prevKey);
                        val = Mathf.Lerp(prevVal, entry.Value, t);
                    } else {
                        val = entry.Value;
                    }
                    int ticksAgo = Mathf.FloorToInt(storedMaxX - dataTick);
                    return $"{Mathf.RoundToInt(val)}\n{Units.TicksAgo(ticksAgo)}";
                }
                prevKey = entry.Key;
                prevVal = entry.Value;
                hasPrev = true;
            }

            if (hasPrev) {
                int ticksAgo = Mathf.FloorToInt(storedMaxX - dataTick);
                return $"{prevVal}\n{Units.TicksAgo(ticksAgo)}";
            }
            return null;
        }

        private void ClearLabels() {
            foreach (var label in labels) {
                PrefabPool.Despawn(label.gameObject);
            }
            labels.Clear();
        }

        private void ReleaseTexture() {
            if (tex != null) {
                RenderTexture.ReleaseTemporary(tex);
                tex = null;
            }
            image.texture = null;
        }

        private void EnsureTexture() {
            var rect = image.rectTransform.rect;
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            if (tex != null && (tex.width != w || tex.height != h)) {
                ReleaseTexture();
            }
            if (tex == null) {
                tex = RenderTexture.GetTemporary(w, h, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
                image.texture = tex;
            }
        }

        private void Render() {
            if (tex == null) { return; }
            ClearLabels();

            var history = def == null ? null : A.S.Query.StockHistory.Ask(def);
            if (history == null || history.Count < 1) {
                ClearBackground();
                HasValidData = false;
                return;
            }

            long minTime = G.Ticks - Consts.TicksPerWeek;
            if (minTime < 0) { minTime = 0; }

            // First pass: calculate bounds
            float minX = float.MaxValue, maxX = float.MinValue;
            float maxY = float.MinValue;
            long prevTick = 0;
            int prevVal = 0;
            bool hasPrev = false;
            int validCount = 0;

            foreach (var entry in history) {
                if (entry.Key < minTime) {
                    prevTick = entry.Key;
                    prevVal = entry.Value;
                    hasPrev = true;
                    continue;
                }
                if (hasPrev && validCount == 0) {
                    // Clamp to minTime so the promoted left edge stays on-canvas.
                    long clamped = prevTick < minTime ? minTime : prevTick;
                    if (clamped < minX) { minX = clamped; }
                    if (prevVal > maxY) { maxY = prevVal; }
                }
                float x = entry.Key;
                float y = entry.Value;
                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y > maxY) { maxY = y; }
                validCount++;
                prevTick = entry.Key;
                prevVal = entry.Value;
                hasPrev = true;
            }

            // Normalize degenerate X range: flat-stock defs may have only one point.
            if (maxX <= minX) {
                minX = minTime;
                maxX = G.Ticks;
            }
            if (maxY < 0f) {
                maxY = hasPrev ? prevVal : 0f;
            }

            float minY = 0f;
            if (Maths.Approximately(minY, maxY)) {
                maxY = maxY > 0f ? maxY * 1.1f : 10f;
            }
            float padY = (maxY - minY) * 0.1f;
            maxY += padY;

            float rangeX = maxX - minX;
            float rangeY = maxY - minY;

            float chartLeft = PadLeft;
            float chartRight = tex.width - PadRight;
            float chartBottom = PadBottom;
            float chartTop = tex.height - PadTop;
            float chartWidth = chartRight - chartLeft;
            float chartHeight = chartTop - chartBottom;

            if (chartWidth <= 0 || chartHeight <= 0) {
                ClearBackground();
                HasValidData = false;
                return;
            }

            float scaleX = chartWidth / rangeX;
            float scaleY = chartHeight / rangeY;

            storedMinX = minX;
            storedMaxX = maxX;
            storedScaleX = scaleX;
            HasValidData = true;

            // Begin GL rendering
            RenderTexture.active = tex;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, tex.width, 0, tex.height);
            GL.Clear(false, true, bgColor);

            GL.Begin(GL.QUADS);
            RenderUtils.LineMaterial.SetPass(0);

            DrawHorizontalGrid(minY, maxY, rangeY, scaleY,
                chartLeft, chartRight, chartBottom, chartTop);
            DrawVerticalGrid(minX, maxX, scaleX,
                chartLeft, chartBottom, chartTop);

            // Chart line from history data
            GL.Color(lineColor);

            if (validCount == 0) {
                // No in-window entries — draw a flat horizontal line at the carried value.
                int flatVal = hasPrev ? prevVal : 0;
                float flatY = (flatVal - minY) * scaleY + chartBottom;
                RenderUtils.DrawQuadLine(
                    new Vector3(chartLeft, flatY, 0),
                    new Vector3(chartRight, flatY, 0),
                    LineThickness);
            } else {
                hasPrev = false;
                float prevDrawX = 0f, prevDrawY = 0f;
                bool drewAny = false;
                foreach (var entry in history) {
                    if (entry.Key < minTime) {
                        // Clamp to minTime so the carry-in segment stays on-canvas.
                        prevDrawX = (minTime - minX) * scaleX + chartLeft;
                        prevDrawY = (entry.Value - minY) * scaleY + chartBottom;
                        hasPrev = true;
                        continue;
                    }
                    float drawX = (entry.Key - minX) * scaleX + chartLeft;
                    float drawY = (entry.Value - minY) * scaleY + chartBottom;
                    if (hasPrev) {
                        RenderUtils.DrawQuadLine(
                            new Vector3(prevDrawX, prevDrawY, 0),
                            new Vector3(drawX, drawY, 0),
                            LineThickness);
                        drewAny = true;
                    }
                    prevDrawX = drawX;
                    prevDrawY = drawY;
                    hasPrev = true;
                }
                if (!drewAny && hasPrev) {
                    // Single in-window point with no prior context — extend flat to chartLeft.
                    RenderUtils.DrawQuadLine(
                        new Vector3(chartLeft, prevDrawY, 0),
                        new Vector3(prevDrawX, prevDrawY, 0),
                        LineThickness);
                }
            }

            GL.End();
            GL.PopMatrix();
            RenderTexture.active = null;
        }

        private void DrawHorizontalGrid(float minY, float maxY,
                float rangeY, float scaleY, float chartLeft, float chartRight,
                float chartBottom, float chartTop) {
            float step = RTChart.FindClosestGradient(rangeY);
            step = Maths.Max(1f, Mathf.Floor(step));
            GL.Color(gridColor);
            float start = Mathf.CeilToInt(minY / step) * step;
            int iterations = 0;
            for (float y = start; y < maxY; y += step) {
                if (iterations++ > 20) { break; }
                float drawY = (y - minY) * scaleY + chartBottom;
                if (drawY < chartBottom + 5 || drawY > chartTop - 10) {
                    continue;
                }
                RenderUtils.DrawQuadLine(
                    new Vector3(chartLeft, drawY, 0),
                    new Vector3(chartRight, drawY, 0),
                    GridThickness);
                if (labelPrefab != null) {
                    var label = PrefabPool.Spawn(labelPrefab, Consts.V2Zero,
                        Consts.QId, image.transform);
                    labels.Add(label);
                    label.SetText(Units.AbbrevNum(y), new Vector2(0, drawY));
                }
            }
        }

        private void DrawVerticalGrid(float minX, float maxX,
                float scaleX, float chartLeft, float chartBottom,
                float chartTop) {
            GL.Color(gridColor);
            int iterations = 0;
            long step = Consts.TicksPerDay;
            for (float x = maxX; x > minX; x -= step) {
                iterations++;
                if (iterations > 10) { break; }
                float drawX = (x - minX) * scaleX + chartLeft;
                if (drawX < chartLeft + 20 || drawX > tex.width - PadRight) {
                    continue;
                }
                RenderUtils.DrawQuadLine(
                    new Vector3(drawX, chartBottom, 0),
                    new Vector3(drawX, chartTop, 0),
                    GridThickness);
                if (iterations > 1 && labelPrefab != null) {
                    var label = PrefabPool.Spawn(labelPrefab, Consts.V2Zero,
                        Consts.QId, image.transform);
                    labels.Add(label);
                    long ticksAgo = (long)(maxX - x);
                    label.SetText(Units.TicksToTime(ticksAgo, true),
                        new Vector2(drawX, 0));
                }
            }
        }

        private void ClearBackground() {
            if (tex == null) { return; }
            RenderTexture.active = tex;
            GL.Clear(false, true, bgColor);
            RenderTexture.active = null;
        }
    }
}
