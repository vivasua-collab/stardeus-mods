#if UNITY_EDITOR
// #define DEBUG_ATMO
// #define SIMULATE_IMMEDIATE
#endif
using System.Collections.Generic;
using Game.Components;
using Game.Constants;
using Game.Data;
using KL.Clock;
using KL.Collections;
using KL.Utils;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Game.Systems.Hints;
using Game.Tools;
using Game.CodeGen;
using Game.Rendering;

using Game.Utils;
using Game.Visuals;
using KL.Grid;
namespace Game.Systems.Atmo {

    public sealed class AtmoSys : GameSystem, IOverlayProvider {
        public const string SysId = "AtmoSys";
        public override string Id => SysId;
        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new AtmoSys());
        }
        private int size;
        private bool shownVentHint;
        private RenderTexture rtViz;
        private ComputeBuffer buffViz;
        private ComputeShader compute;
        private int csViz;
        private bool needsVizUpdate;
        private int csGroupsX;
        private int csGroupsY;
        // Kernel iterations per step, so gas travels this many cells per step.
        // 1 reproduces the original single-pass job
        private int iterations = 1;
        private JobHandle jobO2;
        private JobHandle jobTemp;

        private HeatMapBuffer heatMap;
        public HeatMapBuffer HeatMap => heatMap;
        private Material heatDistortMaterial;
        private bool needsHeatMapUpdate;

        private ComputeShader computeAirtight;
        private int csAirtightViz;
        private bool isShowingAirtightOverlay;
        private bool isShowingInsulationOverlay;
        private ComputeBuffer airtightFlagBuffer;
        private int[] airtightFlagData;
        private readonly HashSet<int> leakCells = new();

        public List<OverlayInfo> Overlays { get; } = new();

        private readonly List<VentComp> vents = new();

        private void AddVent(VentComp vent) {
            if (vent == null) {
                D.Err("Trying to add null vent!");
                return;
            }
            if (!vents.AddUniqueInefficient(vent)) {
                D.Err("Vent was already added! {0}", vent.Entity);
            }
        }
        private void RemoveVent(VentComp vent) {
            if (vent == null) {
                D.Err("Trying to remove null vent!");
                return;
            }
            vents.Remove(vent);
        }

        protected override void OnInitialize() {
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            S.Sig.RecalculateAtmosphere.AddListener(OnRecalculateAtmosphere);
            S.Sig.RebuildMap.AddListener(RebuildAll);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Sig.VentChanged.AddListener(OnVentChanged);
            S.Sig.AreasRecalculated.AddListener(OnAreasRecalculated);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
        }

        private void OnSettingChanged(string setting) {
            if (setting == Pref.GHeatDistortion) {
                if (The.Prefs.GetBool(Pref.GHeatDistortion, PrefH.GHeatDistortion, true)) {
                    if (heatDistortMaterial == null && size > 0) {
                        CreateHeatDistortion();
                    }
                } else {
                    ReleaseHeatDistortion();
                }
                UpdateHeatMap();
            } else if (setting == Pref.GFloorShading) {
                UpdateHeatMap();
            }
        }

        // Kept while anything draws from it: heat distortion, or floor shading (frost and
        // red-hot surfaces)
        private void UpdateHeatMap() {
            var isNeeded = heatDistortMaterial != null || TileShading.IsOn;
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) { isNeeded = false; }
            #endif
            if (isNeeded && heatMap == null && size > 0) {
                heatMap = new HeatMapBuffer(S);
            } else if (!isNeeded && heatMap != null) {
                heatMap.Dispose();
                heatMap = null;
            }
        }

        private void OnAreasRecalculated() {
            if (isShowingAirtightOverlay || isShowingInsulationOverlay) {
                needsVizUpdate = true;
            }
        }

        private void OnVentChanged(VentComp vent, bool added) {
            if (added) {
                AddVent(vent);
            } else {
                RemoveVent(vent);
            }
        }

        private OverlayInfo showingOverlay;
        private AtmoLayer showingLayer;
        private bool isShowingResLayer;
        private NativeArray<float> showingLayerData;
        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (!info.Id.StartsWith("Atmo")) { return; }
            if (showingOverlay == info) {
                if (on) { return; }
                showingOverlay = null;
                showingLayer = null;
                isShowingAirtightOverlay = false;
                isShowingInsulationOverlay = false;
            } else {
                if (!on) { return; }
                showingOverlay = info;
                ConfigureViz();
            }
        }

        private void OnEntityAdded(Entity entity) {
            if (entity is Tile tile) {
                if (tile.Integrity != null) {
                    OnRecalculateAtmosphere(tile.PosIdx);
                }
            }
        }

        private void RebuildAll() {
            var cc = S.Grid.CellCount;
            for (int i = 0; i < cc; i++) {
                OnRecalculateAtmosphere(i);
            }
        }

        private void OnRecalculateAtmosphere(int posIdx) {
            var floor = EntityUtils.FloorAt(posIdx);
            var wall = EntityUtils.WallAt(posIdx);
            var isFloor = EntityUtils.IsPresent(floor);
            var isWall = EntityUtils.IsPresent(wall);
            var floorInsulation = isFloor ? floor.Integrity.Insulation : 0;
            var wallInsulation = isWall ? wall.Integrity.Insulation : 0;
            var floorAirtight = isFloor ? floor.Integrity.Airtight : 0;
            var wallAirtight = isWall ? wall.Integrity.Airtight : 0;
            var insulation = isWall ? wallInsulation : floorInsulation;
            var airtight = isWall ? wallAirtight : floorAirtight;
            S.SetTempResistance(posIdx, insulation);
            S.SetO2Resistance(posIdx, airtight);
            if (isShowingResLayer) {
                needsVizUpdate = true;
            }
        }


        private void OnLoadSave(GameState state) {
            size = state.GridWidth * state.GridHeight;
            // D.Log("Equilibrium: Game: {0}", gameId);
            state.Clock.OnTick.AddListener(OnTick);
            state.Clock.OnUpdate60.AddListener(OnUpdate60);
            state.Sig.EveryFrame.AddListener(EveryFrame);
            S.Sig.EntityAdded.AddListener(OnEntityAdded);
            CreateOverlays();
            RebuildAll();
        }

        private void OnUpdate60() {
            if (!needsVizUpdate && !needsHeatMapUpdate) { return; }
            if (!GameState.IsCurrent(S)) { return; }
            if (needsVizUpdate && showingLayer != null) {
                buffViz.SetData(showingLayerData);
                if (isShowingAirtightOverlay) {
                    ComputeSealFlags(S.Map.O2.Res, Consts.WallMinAirtight, true);
                    computeAirtight.Dispatch(csAirtightViz, csGroupsX, csGroupsY, 1);
                } else if (isShowingInsulationOverlay) {
                    ComputeSealFlags(S.Map.Temp.Res, AreaSealing.WallMinInsulated, false);
                    computeAirtight.Dispatch(csAirtightViz, csGroupsX, csGroupsY, 1);
                } else {
                    compute.Dispatch(csViz, csGroupsX, csGroupsY, 1);
                }
            }
            if (needsHeatMapUpdate && heatMap != null) {
                heatMap.Upload(S);
            }
            needsVizUpdate = false;
            needsHeatMapUpdate = false;
        }

        private void ReleaseResources() {
            if (buffViz != null) {
                rtViz.Release();
                The.Alloc.Unregister(rtViz);
                buffViz.Release();
                The.Alloc.Unregister(buffViz);
                airtightFlagBuffer.Release();
                The.Alloc.Unregister(airtightFlagBuffer);
                airtightFlagBuffer = null;
                airtightFlagData = null;
            }
            ReleaseHeatDistortion();
            heatMap?.Dispose();
            heatMap = null;
            Overlays.Clear();
        }

        private void ReleaseHeatDistortion() {
            FullScreenPassVFXRendererFeature.RemovePassHeat();
            if (heatDistortMaterial != null) {
                UnityEngine.Object.Destroy(heatDistortMaterial);
                heatDistortMaterial = null;
            }
        }

        private void CreateOverlays() {
            if (!GameState.IsCurrent(S)) { return; }
            ReleaseResources();

            compute = Resources.Load<ComputeShader>("Shaders/Compute/AtmoViz");
            csViz = compute.FindKernel("CSAtmoViz");
            rtViz = CreateTex("AtmoVizTex");
            buffViz = CreateBuffer("AtmoO2Buff");

            compute.GetKernelThreadGroupSizes(0, out var x, out var y, out var _);
            csGroupsX = S.GridWidth / (int) x;
            csGroupsY = S.GridHeight / (int) y;

            compute.SetTexture(csViz, "rt", rtViz);
            compute.SetBuffer(csViz, "data", buffViz);
            compute.SetInt("size", size);
            compute.SetInt("gridHeight", S.GridHeight);

            Overlays.Add(new OverlayInfo(OverlayType.Visual,
                    OverlayId.AtmoTemp, IconId.WTemperature,
                ref rtViz, true)
                    .WithTooltipText(T.OverlayTipHeat));

            Overlays.Add(new OverlayInfo(OverlayType.Visual,
                    OverlayId.AtmoO2, IconId.WOverlayOxygen,
                ref rtViz, true)
                    .WithTooltipText(T.OverlayTipOxygen));


            Overlays.Add(new OverlayInfo(OverlayType.Visual,
                    OverlayId.AtmoTempRes, IconId.WInsulation,
                ref rtViz, true)
                    .WithTooltipText(T.OverlayTipInsulation));

            airtightFlagData = new int[size];
            airtightFlagBuffer = new ComputeBuffer(size, sizeof(int));
            airtightFlagBuffer.name = "AirtightFlagBuff";
            The.Alloc.Register(airtightFlagBuffer);

            computeAirtight = Resources.Load<ComputeShader>("Shaders/Compute/AirtightViz");
            csAirtightViz = computeAirtight.FindKernel("CSAirtightViz");
            computeAirtight.SetTexture(csAirtightViz, "rt", rtViz);
            computeAirtight.SetBuffer(csAirtightViz, "data", buffViz);
            computeAirtight.SetBuffer(csAirtightViz, "airtightFlags", airtightFlagBuffer);
            computeAirtight.SetInt("size", size);
            computeAirtight.SetInt("gridHeight", S.GridHeight);
            computeAirtight.SetFloat("spaceRes", AtmoRes.Space);
            computeAirtight.SetVector("colorWall",             new Color(0.10f, 0.30f, 0.85f, 0.65f));
            computeAirtight.SetVector("colorPartialWall",      new Color(0.10f, 0.30f, 0.85f, 0.25f));
            computeAirtight.SetVector("colorEnclosedAirtight", new Color(0.15f, 0.85f, 0.25f, 0.50f));
            computeAirtight.SetVector("colorEnclosedLeaking",  new Color(0.90f, 0.75f, 0.00f, 0.55f));
            computeAirtight.SetVector("colorExposed",          new Color(0.75f, 0.40f, 0.10f, 0.55f));
            computeAirtight.SetVector("colorDamaged",          new Color(1.00f, 0.00f, 0.00f, 1.00f));
            computeAirtight.SetVector("colorDamagedSealed",    new Color(1.00f, 0.70f, 0.00f, 0.90f));
            computeAirtight.SetVector("colorLeak",             new Color(1.00f, 0.85f, 0.00f, 0.95f));

            var airtightOv = new OverlayInfo(OverlayType.Visual,
                    OverlayId.AtmoO2Res, IconId.WAirtightness,
                ref rtViz, true)
                    .WithTooltipText(T.OverlayTipAirtight);
                    // .WithEnvTooltip((sb, pos) => {
                    //     sb.AppendLine();
                    //     sb.AppendLine();
                    //     sb.Append(Texts.Beautify(T.QuestStepMakeAirtightTip.Split("<br><br>")[0]));
                    // });
            Overlays.Add(airtightOv);


            CreateHeatDistortion();
            UpdateHeatMap();
        }

        private RenderTexture CreateTex(string name) {
            var rt = new RenderTexture(S.GridWidth, S.GridHeight, 0, RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear);
            rt.enableRandomWrite = true;
            rt.name = name;
            rt.Create();
            The.Alloc.Register(rt);
            return rt;
        }

        private ComputeBuffer CreateBuffer(string name) {
            var cb = new ComputeBuffer(size, sizeof(float),
                ComputeBufferType.Default, ComputeBufferMode.SubUpdates);
            cb.name = name;
            The.Alloc.Register(cb);
            return cb;
        }

        private void CreateHeatDistortion() {
            #if !PRODUCTION_BUILD
            if (The.TestsAreRunning) { return; }
            #endif
            if (!The.Prefs.GetBool(Pref.GHeatDistortion, PrefH.GHeatDistortion, true)) {
                return;
            }
            var shader = Shader.Find("Hidden/HeatDistortion");
            if (shader == null) {
                D.Err("HeatDistortion shader not found!");
                return;
            }
            heatDistortMaterial = new Material(shader);
            heatDistortMaterial.SetFloat(ShaderVars._HeatThreshold, Consts.MaxBiologicalComfyTemp);
            heatDistortMaterial.SetFloat(ShaderVars._HeatRange, Tunables.VFXHeatDistortion_Range);
            heatDistortMaterial.SetFloat(ShaderVars._HeatMaxTemp, Tunables.VFXHeatDistortion_MaxTemp);
            heatDistortMaterial.SetFloat(ShaderVars._HeatStrength, Tunables.VFXHeatDistortion_Strength);

            FullScreenPassVFXRendererFeature.EnablePassHeat(heatDistortMaterial);
            needsHeatMapUpdate = true;
        }

        // Flag values as AirtightViz.compute reads them (listed in its header). Both overlays
        // classify wall cells the same way - solid, partial (a harmless opening) or a leak the
        // room's seal fails on - so the warning colour means only the walls that need fixing
        private void ComputeSealFlags(NativeArray<float> res, float wallMin, bool isAirtight) {
            System.Array.Clear(airtightFlagData, 0, size);
            leakCells.Clear();
            foreach (var (areaId, cells) in S.Sys.Areas.Areas) {
                bool isSealed = isAirtight
                    ? AreaSealing.CollectAirLeaks(S, cells, leakCells)
                    : AreaSealing.CollectHeatLeaks(S, cells, leakCells);
                int flag = isSealed ? 1 : 2;
                foreach (var cell in cells) {
                    airtightFlagData[cell] = flag;
                }
            }
            for (int i = 0; i < size; i++) {
                if (S.Sys.Areas.At(i) == -1) {
                    var r = res[i];
                    if (r >= wallMin) {
                        airtightFlagData[i] = 3;
                    } else if (r >= AtmoRes.Space) {
                        airtightFlagData[i] = 4;
                    }
                }
            }
            foreach (var cell in leakCells) {
                airtightFlagData[cell] = 5;
            }
            airtightFlagBuffer.SetData(airtightFlagData);
        }

        private void ConfigureViz() {
            UpdateComputeForOverlay();
            needsVizUpdate = true;
        }

        private void UpdateComputeForOverlay() {
            switch (showingOverlay.Id) {
                case OverlayId.AtmoO2: {
                    showingLayer = S.Map.O2;
                    showingLayerData = S.Map.O2.Data;
                    isShowingResLayer = false;
                    isShowingAirtightOverlay = false;
                    isShowingInsulationOverlay = false;
                    compute.SetVector("colorFrom", Consts.ColorOxygenFrom);
                    compute.SetVector("colorTo", Consts.ColorOxygenTo);
                    compute.SetVector("range", Consts.OxygenRange);
                    compute.SetBool("inverseLerp", true);
                    compute.SetBool("isResistance", false);
                    rtViz.filterMode = FilterMode.Bilinear;
                    break;
                }
                case OverlayId.AtmoTemp: {
                    showingLayer = S.Map.Temp;
                    showingLayerData = S.Map.Temp.Data;
                    isShowingResLayer = false;
                    isShowingAirtightOverlay = false;
                    isShowingInsulationOverlay = false;
                    compute.SetVector("colorFrom", Consts.ColorHeatFrom);
                    compute.SetVector("colorTo", Consts.ColorHeatTo);
                    compute.SetVector("range", Consts.HeatRange);
                    compute.SetBool("inverseLerp", true);
                    compute.SetBool("isResistance", false);
                    rtViz.filterMode = FilterMode.Bilinear;
                    break;
                }
                case OverlayId.AtmoO2Res: {
                    showingLayer = S.Map.O2;
                    showingLayerData = S.Map.O2.Res;
                    isShowingResLayer = true;
                    isShowingAirtightOverlay = true;
                    isShowingInsulationOverlay = false;
                    rtViz.filterMode = FilterMode.Point;
                    break;
                }
                case OverlayId.AtmoTempRes: {
                    showingLayer = S.Map.Temp;
                    showingLayerData = S.Map.Temp.Res;
                    isShowingResLayer = true;
                    isShowingAirtightOverlay = false;
                    isShowingInsulationOverlay = true;
                    rtViz.filterMode = FilterMode.Point;
                    break;
                }
            }
        }

        private static readonly int clockLockKey = Hashes.S("AtmoSim");
        private bool isSimRunning;

        private void Simulate() {
            bool isFastSpeed = G.ClockSpeed > ClockSpeed.Fast1;
            #if SIMULATE_IMMEDIATE
            isFastSpeed = true;
            #endif
            float accumulation = S.BB.Get(BBKeyH.HeatAccumulationPerTick, 0f) * 10f;
            // Read per step rather than once at load so a Tunable.Override takes effect live
            iterations = Mathf.Clamp(Tunables.AtmoIterationsPerStep, 1, AtmoChunkJob.MaxIterations);
            if (isFastSpeed) {
                #if DEBUG_ATMO
                D.Log("Starting atmo sim (immediate)");
                #endif
                SimulateLayerImmediate(S.Map.O2, 0f);
                SimulateLayerImmediate(S.Map.Temp, accumulation);
                #if DEBUG_ATMO
                D.Log("Ending atmo sim (immediate)");
                #endif
            } else {
                #if DEBUG_ATMO
                D.Log("Starting atmo sim (non-immediate)");
                #endif
                if (!S.Clock.TryLock(clockLockKey, "AtmoSim")) {
                    return;
                }
                Ready.AtmoO2 = false;
                Ready.AtmoTemp = false;
                jobO2 = StartSimulate(S.Map.O2, 0f);
                jobTemp = StartSimulate(S.Map.Temp, accumulation);
                isSimRunning = true;
            }
        }

        private void EveryFrame() {
            if (isSimRunning) {
                #if DEBUG_ATMO
                D.Log("Ending atmo sim (non-immediate)");
                #endif
                EndSimulate(S.Map.O2, jobO2);
                EndSimulate(S.Map.Temp, jobTemp);
                S.Clock.Unlock(clockLockKey);
                Ready.AtmoO2 = true;
                Ready.AtmoTemp = true;
                isSimRunning = false;
            }
        }

        private void SimulateLayerImmediate(AtmoLayer layer, float accumulation) {
            // var startTime = Time.realtimeSinceStartupAsDouble;
            var handle = StartSimulate(layer, accumulation);
            EndSimulate(layer, handle);
            // var endTime = Time.realtimeSinceStartupAsDouble;
            // D.Warn("Atmo sim took {0} seconds for layer {1}",
            //     endTime - startTime, layer.Id);
        }

        private JobHandle StartSimulate(AtmoLayer layer, float accumulation) {
            // We have 4 frames until these get wiped out
            layer.PrepareForJob();
            if (layer.ActiveChunks.Length == 0) {
                return default;
            }
            var job = new AtmoChunkJob {
                ActiveChunks = layer.ActiveChunks.AsArray(),
                In = layer.DataIn,
                Res = layer.ResIn,
                Pin = layer.Pin,
                Out = layer.DataOut,
                ChunkHasGas = layer.ChunkHasGas,
                GridWidth = layer.Width,
                GridHeight = layer.Height,
                ChunksY = layer.ChunksY,
                Iterations = iterations,
                LossInSpace = layer.SpaceLoss,
                Accumulation = accumulation,
            };
            // Don't try to optimize the parameters! They are like this for a reason.
            return job.ScheduleParallel(layer.ActiveChunks.Length, 1, default);
        }

        private void EndSimulate(AtmoLayer layer, JobHandle handle) {
            handle.Complete();
            layer.FlushData();
            S.Sig.AtmoRecalculated.Send(layer);
            if (!isShowingResLayer && showingLayer != null) {
                needsVizUpdate = true;
            }
            if (layer == S.Map.Temp) {
                needsHeatMapUpdate = true;
            }
        }

        private void OnTick(long tick) {
            if (!Ready.Gen || !Ready.Save) { return; }
            var mod = tick % 10;
            if (mod == 7) {
                if (vents.Count > 0) {
                    UpdateVents();
                }
            } else if (mod == 8) {
                using (D.Timer("AtmoSim", 0.01f)) {
                    Simulate();
                }
            } else if (mod == 9) {
                // Producers register pins on the electricity tick (mod 0) and the step
                // at mod 8 consumes them. Clearing on a tick of their own, rather than
                // inside the step, means a device that lost power or fuel simply stops
                // re-registering, and the list cannot grow if a step is skipped
                S.Map.O2.ClearPins();
                S.Map.Temp.ClearPins();
            }
        }


        private void UpdateVents() {
            var hadExchangeWithOpenSpace = false;
            for (int i = vents.Count - 1; i >= 0; i--) {
                VentComp v = vents[i];
                if (v == null) {
                    D.Err("Null vent in update vents!");
                } else {
                    hadExchangeWithOpenSpace |= v.Equalize();
                }
            }
            if (!shownVentHint && hadExchangeWithOpenSpace) {
                shownVentHint = true;
                S.Sys.Hint.Show(HintVentControls.Id, false);
            }
        }


        public override void Unload() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            vents.Clear();
            ReleaseResources();
            EveryFrame();
        }
    }
}
