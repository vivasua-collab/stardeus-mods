#if !PRODUCTION_BUILD
// #define VIZ_ASTAR
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Tools;
using KL.Grid;
using KL.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Game.Systems.Path {
    public sealed class PathOverlay {
        public bool NeedsVisualUpdate;
        private RenderTexture pathTexture;
        private ComputeShader overlayShader;
        private int overlayKernel;
        private ComputeBuffer navGridBuffer;
        private ComputeBuffer colorsR;
        private ComputeBuffer colorsB;
        private ComputeBuffer colorsG;
        private ComputeBuffer colorsA;
        private readonly GameState S;
        private readonly PathSys sys;
        public PathOverlay(PathSys sys, GameState state) {
            this.sys = sys;
            this.S = state;
            size = state.GridSize;
            state.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            NeedsVisualUpdate = true;
        }

        private bool isShowingOverlay;

        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!on || info.Id != OverlayId.Path) {
                isShowingOverlay = false;
            } else {
                isShowingOverlay = true;
                if (NeedsVisualUpdate) {
                    NeedsVisualUpdate = false;
                    UpdateOverlay();
                }
            }
        }

        private int[] navGridData;
        private float[] colorsAData;
        private readonly int size;
        public void TryUpdate() {
            if (isShowingOverlay && NeedsVisualUpdate) {
                NeedsVisualUpdate = false;
                UpdateOverlay();
            }
        }

        private void UpdateOverlay() {
            UpdateOverlayData();
            navGridBuffer.SetData(navGridData);
            colorsA.SetData(colorsAData);
            overlayShader.Dispatch(overlayKernel, groupsX, groupsY, 1);
        }

        private void UpdateOverlayData() {
            for (int i = 0; i < size; i++) {
                var node = sys.Nodes[i];
                // #if !PRODUCTION_BUILD
                // if (node.IsWorkSpot) {
                //     colorsAData[i] = 0.5f;
                //     navGridData[i] = 3;
                //     continue;
                // }
                // #endif
                if (node.IsTeleporter) {
                    colorsAData[i] = node.Difficulty;
                    navGridData[i] = 3;
                } else if (node.IsPassable) {
                    colorsAData[i] = node.Difficulty;
                    if (node.IsWalkable) {
                        navGridData[i] = 2;
                    } else {
                        // flyable
                        navGridData[i] = 1;
                    }
                } else {
                    navGridData[i] = 0;
                    colorsAData[i] = 2f; // will become 1f in shader
                }
            }

            foreach (var claim in sys.ClaimedPosBeingToPosMap) {
                var v = claim.Value;
                if (v != Pos.Invalid) {
                    navGridData[v] = 4;
                    colorsAData[v] = 0f;
                }
            }

            #if VIZ_ASTAR
            float min = float.MaxValue;
            float max = float.MinValue;
            D.Warn("Crawled: {0}", sys.CrawledPositions.Count);
            foreach (var pos in sys.CrawledPositions) {
                min = math.min(pos.Value, min);
                max = math.max(pos.Value, max);
                // D.Warn("Astar weight: {0} -> {1}", Pos.String(pos.Key), pos.Value);
            }
            // D.Warn("Min-max astar weight: {0} -> {1}", min, max);
            var origMin = min;
            min = math.lerp(min, max, 0.1f);
            max = math.lerp(max, origMin, 0.1f);
            foreach (var pos in sys.CrawledPositions) {
                navGridData[pos.Key] = 4;
                var alpha = Mathf.InverseLerp(min, max, pos.Value);
                colorsAData[pos.Key] = alpha * alpha * alpha * alpha;
            }
            #endif
        }

        public void ReleaseResources() {
            if (navGridBuffer != null) {
                colorsR.Release();
                colorsG.Release();
                colorsB.Release();
                colorsA.Release();
                navGridBuffer.Release();
                pathTexture.Release();

                The.Alloc.Unregister(colorsR);
                The.Alloc.Unregister(colorsG);
                The.Alloc.Unregister(colorsB);
                The.Alloc.Unregister(colorsA);
                The.Alloc.Unregister(navGridBuffer);
                The.Alloc.Unregister(pathTexture);
            }
        }

        private int groupsX;
        private int groupsY;

        public void PrepareCompute(List<OverlayInfo> overlays) {
            if (overlayShader == null) {
                overlayShader = Resources.Load<ComputeShader>(
                    "Shaders/Compute/PathVisualize");
                overlayKernel = overlayShader.FindKernel("CSPathVisualize");
            }
            overlayShader.GetKernelThreadGroupSizes(0,
                out var x, out var y, out var _);
            groupsX = S.GridWidth / (int) x;
            groupsY = S.GridHeight / (int) y;

            navGridData = new int[size];
            colorsAData = new float[size];

            navGridBuffer = new ComputeBuffer(size, sizeof(int));
            navGridBuffer.name = "NavGridBuffer";
            The.Alloc.Register(navGridBuffer);
            // default, passable, walkable, p+w
            colorsR = new ComputeBuffer(5, sizeof(float));
            colorsR.name = "PathSysColorsR";
            colorsG = new ComputeBuffer(5, sizeof(float));
            colorsG.name = "PathSysColorsG";
            colorsB = new ComputeBuffer(5, sizeof(float));
            colorsB.name = "PathSysColorsB";
            colorsA = new ComputeBuffer(size, sizeof(float));
            colorsA.name = "PathSysColorsA";
            The.Alloc.Register(colorsR);
            The.Alloc.Register(colorsG);
            The.Alloc.Register(colorsB);
            The.Alloc.Register(colorsA);

            // 0 - not passable
            // 1 - passable, not walkable
            // 2 - walkable, passable
            // 3 - teleporter
            // 4 - claimed
            var colR = new float[5] { 0, 1, 0, 1, 0.0f, };
            var colG = new float[5] { 0, 0, 1, 0, 0.8f, };
            var colB = new float[5] { 0, 0, 0, 1, 1.0f, };

            colorsR.SetData(colR);
            colorsB.SetData(colB);
            colorsG.SetData(colG);

            overlayShader.SetInt("size", size);
            overlayShader.SetInt("gridHeight", S.GridHeight);

            overlayShader.SetBuffer(overlayKernel, "gridData", navGridBuffer);

            overlayShader.SetBuffer(overlayKernel, "colorsR", colorsR);
            overlayShader.SetBuffer(overlayKernel, "colorsG", colorsG);
            overlayShader.SetBuffer(overlayKernel, "colorsB", colorsB);
            overlayShader.SetBuffer(overlayKernel, "colorsA", colorsA);

            pathTexture = new RenderTexture(
                S.GridWidth, S.GridHeight, 0,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            pathTexture.filterMode = FilterMode.Point;
            pathTexture.enableRandomWrite = true;
            pathTexture.name = "PathSysTex";
            pathTexture.Create();
            The.Alloc.Register(pathTexture);
            bool needsEnvTip = true;
            #if VIZ_ASTAR
            needsEnvTip = false;
            #endif
            overlays.Add(new OverlayInfo(OverlayType.Visual,
                    OverlayId.Path, IconId.WFoot,
                ref pathTexture, needsEnvTooltip: needsEnvTip)
                    .WithTooltipText(T.OverlayTipPathfinding));

            overlayShader.SetTexture(overlayKernel, "pathTex", pathTexture);
        }
    }
}
