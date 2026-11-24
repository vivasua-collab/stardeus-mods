using KL.Grid;
using KL.Utils;
using System;
using Game.Constants;
using UnityEngine;
using Game.Data;
using System.Threading;
using System.Threading.Tasks;
using Game.Components;
using KL.Clock;
using System.Collections.Generic;
using Game.Utils;
using Game.CodeGen;
using System.Collections;
using KL.Randomness;
using Game.UI;
using Game.Tools;

namespace Game.Systems.Plan {
    public sealed class PlanningSys : GameSystem, IOverlayProvider {
        public const string SysId = "PlanningSys";
        public override string Id => SysId;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new PlanningSys());
        }
        private RenderTexture planTexture;
        private ComputeShader overlayShader;
        private PlanningUI ui;
        private int overlayKernel;
        private ComputeBuffer planBuffer;
        private ComputeBuffer colorsR;
        private ComputeBuffer colorsB;
        private ComputeBuffer colorsG;
        private ComputeBuffer colorsA;
        private int groupsX;
        private int groupsY;

        private int[] planData;
        private float[] colorsAData;
        private bool needsVisualUpdate;

        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;

        private Grid<int> grid => S.Map.Plan;

        protected override void OnInitialize() {
            S.Sig.UpdatePlan.AddListener(OnUpdatePlan);
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
        }

        private void OnLoadSave(GameState state) {
            ui = new PlanningUI(state);
            PrepareCompute();
            state.Sig.EveryFrame.AddListener(EveryFrame);
            state.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
        }

        private bool isShowingOverlay;

        private void OnToggleOverlay(OverlayInfo ov, bool on) {
            if (ov.IdH == OverlayIdH.Plan && on) {
                if (!isShowingOverlay) {
                    isShowingOverlay = true;
                    if (The.Toolbox.IsFreeSelect) {
                        var planTool = The.Toolbox.Get<ToolPlan>(ToolPlan.ToolId);
                        The.Toolbox.SetActive(planTool);
                    }
                    ui.Show();
                }
            } else if (isShowingOverlay) {
                isShowingOverlay = false;
                ui.Close();
                if (UIShowing.BuildMenu) {
                    // S.UIQuery.ShowBuildMenu.Ask(false);
                    // S.Sig.HideToolMenu.Send();
                }
                if (The.Toolbox.ActiveTool is ToolPlan) {
                    The.Toolbox.Clear();
                }
            }
        }

        /*
        Apparently people want to keep plans after building stuff
        private void OnEntityAdded(Entity entity) {
            if (entity is Tile t && (t.Definition.LayerId == WorldLayer.Walls ||
                    t.Definition.LayerId == WorldLayer.Objects)) {
                // Remove plan
                t.Transform.ForEachMulti((p) => {
                    state.Map.Plan.Data[p] = 0;
                });
                needsVisualUpdate = true;
            }
        }
        */

        private void OnUpdatePlan() {
            needsVisualUpdate = true;
        }

        private void ReleaseResources() {
            if (colorsR != null) {
                colorsR.Release();
                The.Alloc.Unregister(colorsR);
            }
            if (colorsG != null) {
                colorsG.Release();
                The.Alloc.Unregister(colorsG);
            }
            if (colorsB != null) {
                colorsB.Release();
                The.Alloc.Unregister(colorsB);
            }
            if (colorsA != null) {
                colorsA.Release();
                The.Alloc.Unregister(colorsA);
            }
            if (planBuffer != null) {
                planBuffer.Release();
                The.Alloc.Unregister(planBuffer);
            }
            if (planTexture != null) {
                planTexture.Release();
                The.Alloc.Unregister(planTexture);
            }
            overlays.Clear();
        }

        private void PrepareCompute() {
            if (!Consts.HasComputeShaders) { return; }
            if (overlayShader is null) {
                overlayShader = Resources.Load<ComputeShader>(
                    "Shaders/Compute/Planning");
                overlayKernel = overlayShader.FindKernel("CSPlanning");
            }
            overlayShader.GetKernelThreadGroupSizes(overlayKernel,
                out var x, out var y, out var _);
            groupsX = S.GridWidth / (int) x;
            groupsY = S.GridHeight / (int) y;

            ReleaseResources();

            var size = S.Map.Size;

            planData = new int[size];
            colorsAData = new float[size];

            planBuffer = new ComputeBuffer(size, sizeof(int));
            planBuffer.name = "PlanBuffer";
            colorsR = new ComputeBuffer(7, sizeof(float));
            colorsR.name = "PlanColorsR";
            colorsG = new ComputeBuffer(7, sizeof(float));
            colorsG.name = "PlanColorsG";
            colorsB = new ComputeBuffer(7, sizeof(float));
            colorsB.name = "PlanColorsB";
            colorsA = new ComputeBuffer(size, sizeof(float));
            colorsA.name = "PlanColorsA";
            The.Alloc.Register(planBuffer);
            The.Alloc.Register(colorsR);
            The.Alloc.Register(colorsG);
            The.Alloc.Register(colorsB);
            The.Alloc.Register(colorsA);

            var colR = new float[7];
            var colG = new float[7];
            var colB = new float[7];

            colR[0] = 0;
            colG[0] = 0;
            colB[0] = 0;
            for (int i = 1; i < 6; i++) {
                #pragma warning disable
                var col = Tunable.IntSet($"PlanningColor{i}");
                #pragma warnign restore
                colR[i] = col[0];
                colG[i] = col[1];
                colB[i] = col[2];
            }
            // idx 6 = no-build zone
            colR[6] = 1f;
            colG[6] = 0f;
            colB[6] = 0f;

            colorsR.SetData(colR);
            colorsB.SetData(colB);
            colorsG.SetData(colG);

            overlayShader.SetInt("size", size);
            overlayShader.SetInt("gridHeight", S.GridHeight);

            overlayShader.SetBuffer(overlayKernel, "gridData", planBuffer);

            overlayShader.SetBuffer(overlayKernel, "colorsR", colorsR);
            overlayShader.SetBuffer(overlayKernel, "colorsG", colorsG);
            overlayShader.SetBuffer(overlayKernel, "colorsB", colorsB);
            overlayShader.SetBuffer(overlayKernel, "colorsA", colorsA);

            planTexture = new RenderTexture(
                    S.GridWidth, S.GridHeight, 0,
                    RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) {
                enableRandomWrite = true,
                name = "PlanTex",
                filterMode = FilterMode.Point
            };
            planTexture.Create();
            The.Alloc.Register(planTexture);
            overlays.Add(new OverlayInfo(OverlayType.Visual,
                OverlayId.Plan, IconId.WPlanning, ref planTexture));

            overlayShader.SetTexture(overlayKernel, "planTex", planTexture);
            needsVisualUpdate = true;
        }

        private void EveryFrame() {
            if (needsVisualUpdate) {
                if (UIShowing.Overlay && UIShowing.OverlayType == OverlayId.Plan) {
                    needsVisualUpdate = false;
                    using (D.Timer("UpdateOverlaySync", 0.0001f)) {
                        UpdateOverlaySync();
                    }
                }
            }
        }

        private bool isUpdatingNow;
        private bool isUnloaded;
        private void UpdateOverlaySync() {
            UpdateOverlayData();
            planBuffer.SetData(planData);
            colorsA.SetData(colorsAData);
            overlayShader.Dispatch(overlayKernel, groupsX, groupsY, 1);
        }

        private IEnumerator UpdateOverlayAsync() {
            while (isUpdatingNow || S.Map == null || S.Map.Plan == null) {
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
                if (isUnloaded) { yield break; }
            }
            isUpdatingNow = true;
            var t = Task.Run(UpdateOverlayData);
            while (!t.IsCompleted) {
                yield return null;
                if (!GameState.IsCurrent(S)) { yield break; }
                if (isUnloaded) { yield break; }
            }
            planBuffer.SetData(planData);
            colorsA.SetData(colorsAData);
            overlayShader.Dispatch(overlayKernel, groupsX, groupsY, 1);
            isUpdatingNow = false;
        }

        private void UpdateOverlayData() {
            var alpha = Tunables.PlanningAlpha;
            for (int i = 0; i < grid.Data.Length; i++) {
                var value = grid.Data[i];
                if (EntityUtils.IsWithinMargin(i, false, false)) {
                    planData[i] = value;
                    colorsAData[i] = alpha;
                } else {
                    // no build zone
                    planData[i] = 6;
                    colorsAData[i] = 0.5f;
                }
            }
        }

        private void Recalculate() {
            needsVisualUpdate = true;
        }

        public override void Unload() {
            isUnloaded = true;
            planData = null;
            colorsAData = null;
            ui = null;
            ReleaseResources();
        }
    }
}
