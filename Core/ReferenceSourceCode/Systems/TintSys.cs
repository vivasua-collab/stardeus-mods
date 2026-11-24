#if UNITY_EDITOR
// #define GENERATE_RANDOM
#endif
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
using Unity.Mathematics;
using Unity.Collections;
using System.Runtime.InteropServices;
using Game.UI;
using Game.Systems.UIUX;
using Game.Tools;
using Game.Visuals;
using Game.Input;
using System.Linq;

namespace Game.Systems {
    public sealed class TintSys : GameSystem, ISaveableSpecial, IOverlayProvider {

        public struct TintFX {
            public TintFX(PosRect rekt, Color color) {
                Rekt = rekt;
                Color = color;
            }
            public readonly PosRect Rekt;
            public readonly Color Color;
        }
        public const string SysId = "TintSys";
        public override string Id => SysId;

        public Entity Entity => null;

        private readonly List<OverlayInfo> overlays = new();
        public List<OverlayInfo> Overlays => overlays;

        [SelfInit]
        private static void Register() {
            GameSystems.Register(SysId, () => new TintSys());
        }

        private int gridSize;
        private TintSysData data;
        private ComputeBuffer tintFloorBuffer;
        private ComputeBuffer tintWallBuffer;
        private NativeArray<Color> tintFloorData;
        private NativeArray<Color> tintWallData;
        private bool[] floorsWithTint;
        private bool[] wallsWithTint;
        private bool needsFloorBufferFlush;
        private bool needsWallBufferFlush;
        public Color CurrentToolColor;
        public byte CurrentToolLayer;
        public string CurrentToolMode;
        internal List<Color> SavedColors;
        private readonly List<TinterComp> tinters = new();
        private TintContextPopup contextPopup;
        internal ToolTint TintTool => tintTool;

        protected override void OnInitialize() {
            contextPopup = new TintContextPopup(S, this);
            S.Sig.ToggleOverlay.AddListener(OnToggleOverlay);
            S.Sig.AfterLoadState.AddListener(OnLoadSave);
            tintTool = The.Toolbox.Get<ToolTint>(ToolTint.ToolId);
        }

        public void AddTinter(TinterComp tinter) {
            tinters.Add(tinter);
            UpdateUIBlock(true);
        }

        public void RemoveTinter(TinterComp tinter) {
            tinters.Remove(tinter);
            UpdateUIBlock(true);
        }
        private const int particlesPerFrame = 16;

        private void FlushPendingParticles() {
            int particlesThisFrame = 0;
            while (particlesThisFrame < particlesPerFrame
                    && pendingParticles.TryDequeue(out var paintBit)) {
                particlesThisFrame++;
                ShowParticlesAt(paintBit);
                UpdateUIBlock(true);
            }
            if (pendingParticles.Count == 0) {
                if (recentlyActiveTinters.Count > 0) {
                    D.Err("No pending particles, but still have recently active tinters: {0}",
                        EntityUtils.DumpPositions(recentlyActiveTinters.Keys));
                    recentlyActiveTinters.Clear();
                }
            }
        }

        private readonly HashSet<TinterComp> usedTinters = new();

        public void ShowParticlesAt(TintFX p) {
            usedTinters.Clear();
            p.Rekt.ForEach((pos) => {
                if (recentlyActiveTinters.Remove(pos, out var t)) {
                    for (int i = t.Count - 1; i >= 0; i--) {
                        TinterComp tinter = t[i];
                        usedTinters.Add(tinter);
                    }
                }
            });

            if (usedTinters.Count == 0) {
                if (CurrentToolMode == "draw") {
                    if (!A.InstantBuild) {
                        D.Err("Recently active tinters did not contain rect: {0}", p.Rekt);
                    }
                }
                // 1. find closest tinter
                var closest = EntityUtils.ClosestMatching(p.Rekt.CenterPos,
                    tinters, (t) => t.IsTinterActive);
                if (closest != null) {
                    usedTinters.Add(closest);
                }
            }

            foreach (var t in usedTinters) {
                var startPos = t.Entity.Position;
                var particle = ParticlesVFX.CreateWithBudget(S, "TintVFX",
                    startPos, BBKeyH.TintPlayingVFX, Consts.TintMaxVFX);
                if (particle.TryGetFX(out var fx)) {
                    var particleDir = p.Rekt.Center() - startPos;
                    //D.Warn("Showing particle: {0} -> {1}", startPos, particleDir);
                    fx.SetVector2(ShaderVars.Size, p.Rekt.Extents);
                    fx.SetVector3(ShaderVars.Target, particleDir);
                    fx.SetVector4(ShaderVars.Color, p.Color);
                    fx.SetFloat(ShaderVars.StartTime, GameTimer.unscaledTime);
                }
                //particle.SetClock(S.Clock);
                particle.DestroyAfterRealTime(10f);
            }
            usedTinters.Clear();
        }

        private void OnLoadSave(GameState state) {
            PrepareCompute();
            RestoreSavedData();
            // if something doesn't feel right when the game is paused / loaded,
            // this could be the cause
            // state.Sig.OnUpdate.AddListener(OnUpdate);
            state.Clock.OnUpdate60.AddListener(OnUpdate);
            state.Sig.EntityRemoved.AddListener(OnEntityRemoved);
        }

        private void CreateInitialColors() {
            foreach (var color in Palette.ForGroup("TileTint")) {
                CurrentToolColor = color.Color;
                CurrentToolColor.a = 0.5f;
                SaveCurrentColor();
                CurrentToolColor = default;
            }
        }

        private void RestoreSavedData() {
            if (data == null) {
                // We never had data - let's fill some custom colors
                CreateInitialColors();
                return;
            }
            CurrentToolColor = data.Color;
            if (CurrentToolColor.a > 0) {
                CurrentToolMode = "draw";
            }
            CurrentToolLayer = data.Layer;
            SavedColors = data.SavedColors;
            try {
                if (data.FloorTint != null) {
                    foreach (var kv in data.FloorTint) {
                        if (EntityUtils.IsFloorAt(kv.Key)) {
                            floorsWithTint[kv.Key] = true;
                            tintFloorData[kv.Key] = kv.Value;
                        } else {
                            D.Err("Floor had tint at {0} but no floor!",
                                Pos.String(kv.Key));
                        }
                    }
                    needsFloorBufferFlush = true;
                }
                if (data.WallTint != null) {
                    foreach (var kv in data.WallTint) {
                        if (EntityUtils.IsWallAt(kv.Key)) {
                            wallsWithTint[kv.Key] = true;
                            tintWallData[kv.Key] = kv.Value;
                        } else {
                            D.Err("Wall had tint at {0} but no floor!",
                                Pos.String(kv.Key));
                        }
                    }
                    needsWallBufferFlush = true;
                }
            } catch (Exception ex) {
                D.LogEx(ex, "Failed loading tint data");
            }
        }

        private void OnEntityRemoved(Entity entity) {
            if (entity.Definition == null) {
                // child tile
                return;
            }
            var layer = entity.Definition.LayerId;
            if (layer < WorldLayer.Objects) {
                RemoveTint(layer, entity.PosIdx, false);
            }
        }

        private bool needsToolCheck;

        private void OnUpdate() {
            if (needsToolCheck) {
                needsToolCheck = false;
                UpdateToolMode();
            }
            if (pendingParticles.Count > 0) {
                FlushPendingParticles();
            }
            if (needsFloorBufferFlush) {
                needsFloorBufferFlush = false;
                tintFloorBuffer.SetData(tintFloorData);
            } else if (needsWallBufferFlush) {
                needsWallBufferFlush = false;
                tintWallBuffer.SetData(tintWallData);
            }
        }

        private void ReleaseResources() {
            Shader.SetGlobalBuffer(ShaderVars._CustomFloorTint, (ComputeBuffer) null);
            Shader.SetGlobalBuffer(ShaderVars._CustomWallTint, (ComputeBuffer) null);
            if (tintFloorBuffer != null) {
                tintFloorBuffer.Release();
                The.Alloc.Unregister(tintFloorBuffer);
                tintWallBuffer.Release();
                The.Alloc.Unregister(tintWallBuffer);
            }
            pendingParticles.Clear();
            if (tintFloorData.IsCreated) {
                tintFloorData.Dispose();
            }
            if (tintWallData.IsCreated) {
                tintWallData.Dispose();
            }
        }

        public void PickToolColor(Color color) {
            CurrentToolColor = color;
            CurrentToolMode = "draw";
            AfterChangeConfig();
            needsToolCheck = true;
        }
        public bool TryGetTint(byte layer, int pos, out Color tint) {
            if (layer == WorldLayer.Floor) {
                return TryGetFloorTint(pos, out tint);
            } else if (layer == WorldLayer.Walls) {
                return TryGetWallTint(pos, out tint);
            }
            tint = default;
            return false;
        }

        public bool TryGetFloorTint(int pos, out Color tint) {
            if (floorsWithTint != null && EntityUtils.IsWithinBounds(pos)
                    && floorsWithTint[pos]) {
                tint = tintFloorData[pos];
                return true;
            }
            tint = default;
            return false;
        }

        public bool TryGetWallTint(int pos, out Color tint) {
            if (wallsWithTint != null && EntityUtils.IsWithinBounds(pos)
                    && wallsWithTint[pos]) {
                tint = tintWallData[pos];
                return true;
            }
            tint = default;
            return false;
        }

        private readonly Queue<TintFX> pendingParticles = new();

        public void ShowParticlesAt(PosRect rect, Color color) {
            if (A.IsCosmeticParticlesOn) {
                pendingParticles.Enqueue(new TintFX(rect, color));
            }
        }

        public void ShowParticlesAt(int pos, Color color) {
            if (A.IsCosmeticParticlesOn) {
                pendingParticles.Enqueue(new TintFX(new PosRect(pos, pos), color));
            }
        }
        private readonly Dictionary<int, List<TinterComp>> recentlyActiveTinters = new();

        public bool TryTint(int layer, int pos, Color color, bool showParticle) {
            bool canTint = A.InstantBuild;
            var eff = Tunables.PainterPigmentEfficiency;
            if (eff == 0) {
                D.Err("PainterPigmentEfficiency cannot be 0! Setting to 1");
                eff = 1f;
            }
            var r = (color.r * color.a) / eff;
            var g = (color.g * color.a) / eff;
            var b = (color.b * color.a) / eff;

            foreach (var tinter in tinters.OrderBy(
                        tt => Pos.SqrDistance(pos, tt.Entity.PosIdx))) {
                if (tinter.TryConsumePigmentFor(ref r, ref g, ref b)) {
                    if (!recentlyActiveTinters.TryGetValue(pos, out var tinters)) {
                        tinters = new List<TinterComp>();
                        recentlyActiveTinters.Add(pos, tinters);
                    }
                    tinters.Add(tinter);
                    if (r < 0.0001f && g < 0.0001f && b < 0.0001f) {
                        // we got all the color
                        canTint = true;
                        break;
                    }
                }
            }
            if (canTint) {
                SetTint(layer, pos, color, showParticle);
            } else {
                recentlyActiveTinters.Remove(pos);
            }
            return canTint;
        }

        public void SetTint(int layer, int pos, Color color, bool showParticle) {
            //D.Log("Setting tint: {0} {1} {2}", layer, Pos.String(pos), color);
            if (layer == WorldLayer.Floor) {
                floorsWithTint[pos] = true;
                tintFloorData[pos] = color;
                needsFloorBufferFlush = true;
                if (showParticle) {
                    ShowParticlesAt(pos, color);
                }
            } else if (layer == WorldLayer.Walls) {
                wallsWithTint[pos] = true;
                tintWallData[pos] = color;
                needsWallBufferFlush = true;
                if (showParticle) {
                    ShowParticlesAt(pos, color);
                }
            } else {
                D.Err("Trying to tint an unsupported layer: {0} at {1}",
                    layer, Pos.String(pos));
            }
        }
        public bool RemoveTint(int layer, int pos, bool AddParticle) {
            if (layer == WorldLayer.Floor) {
                if (floorsWithTint[pos]) {
                    floorsWithTint[pos] = false;
                    tintFloorData[pos] = default;
                    needsFloorBufferFlush = true;
                    if (AddParticle) {
                        ShowParticlesAt(pos, Consts.ColorWhite);
                    }
                    return true;
                }
            } else if (layer == WorldLayer.Walls) {
                if (wallsWithTint[pos]) {
                    wallsWithTint[pos] = false;
                    tintWallData[pos] = default;
                    needsWallBufferFlush = true;
                    if (AddParticle) {
                        ShowParticlesAt(pos, Consts.ColorWhite);
                    }
                    return true;
                }
            } else {
                D.Err("Trying to tint an unsupported layer: {0} at {1}",
                    layer, Pos.String(pos));
            }
            return false;
        }

        private void PrepareCompute() {
            gridSize = S.GridSize;
            var stride = Marshal.SizeOf(new Color());

            tintFloorData = new NativeArray<Color>(gridSize, Allocator.Persistent);
            tintWallData = new NativeArray<Color>(gridSize, Allocator.Persistent);

            floorsWithTint = new bool[gridSize];
            wallsWithTint = new bool[gridSize];

            #if GENERATE_RANDOM
            var rng = new Rng(DateTime.UtcNow.GetHashCode());
            for (int i = 0; i < gridSize; i++) {
                if (EntityUtils.IsFloorAt(i)) {
                    if (rng.Chance(0.5f)) {
                        SetTint(WorldLayer.Floor, i, rng.Color());
                    }
                    if (EntityUtils.IsWallAt(i)) {
                        if (rng.Chance(0.5f)) {
                            SetTint(WorldLayer.Walls, i, rng.Color());
                        }
                    }
                }
            }
            #endif

            tintFloorBuffer = new ComputeBuffer(gridSize, stride);
            The.Alloc.Register(tintFloorBuffer);
            tintWallBuffer = new ComputeBuffer(gridSize, stride);
            The.Alloc.Register(tintWallBuffer);
            tintWallBuffer.SetData(tintWallData);
            tintFloorBuffer.SetData(tintFloorData);

            Shader.SetGlobalBuffer(ShaderVars._CustomFloorTint, tintFloorBuffer);
            Shader.SetGlobalBuffer(ShaderVars._CustomWallTint, tintWallBuffer);

            overlays.Add(new OverlayInfo(OverlayType.Controls,
                OverlayId.Tint, IconId.WPaint));
        }

        public override void Unload() {
            ReleaseResources();
        }

        private ToolTint tintTool;

        private void ConfigureTool() {
            if (CurrentToolMode != "draw" || CurrentToolColor.a > 0) {
                tintTool.Configure(CurrentToolLayer,
                    CurrentToolColor, CurrentToolMode);
            } else if (The.Toolbox.ActiveTool == tintTool) {
                The.Toolbox.Clear();
            }
        }
        private bool hasRemovedUITarget;
        private bool CheckUnlocked() {
            if (S.BB.Get(BBKeyH.PaintUnlocked, false)) {
                if (!hasRemovedUITarget) {
                    hasRemovedUITarget = true;
                    S.Sig.SetUITarget.Send(null, CameraFocus.FlashColor());
                }
                return true;
            }
            if (A.InstantBuild) {
                return true;
            }
            if (UIShowing.Dialog) {
                WPopup.Create(false, IconId.CPaint, T.NotAvailable, T.TintSysLocked).Spawn();
            } else {
                The.Dialog.ShowSimple(IconId.CPaint,
                    T.TintSysLocked);
            }
            return false;
        }

        private bool isShowingOverlay;
        private void OnToggleOverlay(OverlayInfo info, bool on) {
            if (on && !isShowingOverlay && info.Id == OverlayId.Tint) {
                if (!CheckUnlocked()) {
                    return;
                }
                isShowingOverlay = true;
                contextPopup.Show();
                if (CurrentToolColor.a > 0) {
                    ConfigureTool();
                }
                needsToolCheck = true;
            } else if (!on && isShowingOverlay && info.Id == OverlayId.Tint) {
                isShowingOverlay = false;
                contextPopup.Hide();
            }
        }

        public void SaveSpecial(SystemsDataSpecial sd) {
            sd.Tint ??= new TintSysData();
            sd.Tint.Color = CurrentToolColor;
            sd.Tint.Layer = CurrentToolLayer;
            sd.Tint.SavedColors = SavedColors;
            Dictionary<int, Color> floorTints = null;
            Dictionary<int, Color> wallTints = null;
            for (int i = 0; i < gridSize; i++) {
                if (floorsWithTint[i]) {
                    (floorTints ??= new()).Add(i, tintFloorData[i]);
                }
                if (wallsWithTint[i]) {
                    (wallTints ??= new()).Add(i, tintWallData[i]);
                }
            }
            sd.Tint.FloorTint = floorTints;
            sd.Tint.WallTint = wallTints;
        }

        public void LoadSpecial(SystemsDataSpecial sd) {
            if (sd.Tint != null) {
                data = sd.Tint;
            }
        }

        internal void SaveCurrentColor() {
            if (CurrentToolColor.a == 0) {
                return;
            }
            SavedColors ??= new List<Color>();
            if (SavedColors.Contains(CurrentToolColor)) {
                UISounds.PlayActionDenied();
                return;
            }
            SavedColors.Add(CurrentToolColor);
        }

        public void OnToolClose() {
            needsToolCheck = true;
        }

        internal void AfterColorChanged() => AfterChangeConfig();

        internal void RemoveSavedColor(Color c) {
            SavedColors?.Remove(c);
        }

        private void AfterChangeConfig() {
            ConfigureTool();
            contextPopup?.UpdateCurrentColor();
            UpdateUIBlock(true);
            needsToolCheck = true;
        }

        private void UpdateToolMode() {
            contextPopup?.RefreshToolModeIcons();
        }

        public bool IsTintHidden;

        internal void ToggleTint() {
            IsTintHidden = !IsTintHidden;
            if (IsTintHidden) {
                Shader.SetGlobalInt(ShaderVars._SkipTint, 1);
                The.Toolbox.Clear();
            } else {
                tintWallBuffer.SetData(tintWallData);
                tintFloorBuffer.SetData(tintFloorData);
                Shader.SetGlobalInt(ShaderVars._SkipTint, 0);
                ConfigureTool();
            }
        }

        private void ClosePaint() {
            S.Sig.HideOverlay.Send();
            contextPopup.Hide();
        }

        private float r;
        private float g;
        private float b;
        private float max;

        public void UpdateUIBlock(bool wasUpdated) {
            if (wasUpdated && contextPopup?.IsShowing != true) {
                return;
            }
            UpdateLevels();
            contextPopup?.UpdateLevels(r, g, b, max);
        }

        private void UpdateLevels() {
            r = 0f;
            g = 0f;
            b = 0f;
            max = 0f;
            foreach (var t in tinters) {
                if (t.IsTinterActive) {
                    r += t.ColorLevelR;
                    g += t.ColorLevelG;
                    b += t.ColorLevelB;
                    max += t.ColorCapacity;
                }
            }
        }

        public int EstimateRemainingTiles(Color color, bool updateLevels) {
            if (A.InstantBuild) {
                return S.GridSize;
            }
            var eff = Tunables.PainterPigmentEfficiency;
            if (eff == 0) {
                D.Err("PainterPigmentEfficiency cannot be 0! Setting to 1");
                eff = 1f;
            }
            var wr = (color.r * color.a) / eff;
            var wg = (color.g * color.a) / eff;
            var wb = (color.b * color.a) / eff;
            if (updateLevels) {
                UpdateLevels();
            }
            var cr = r;
            var cg = g;
            var cb = b;
            var countR = wr > 0 ? cr / wr : float.MaxValue;
            var countG = wg > 0 ? cg / wg : float.MaxValue;
            var countB = wb > 0 ? cb / wb : float.MaxValue;
            return Mathf.FloorToInt(Maths.Min(Maths.Min(countR, countG), countB));
        }

        internal void SwitchToEyeDropper() {
            if (The.Toolbox.ActiveTool == tintTool) {
                if (CurrentToolMode == "copy") {
                    The.Toolbox.Clear();
                    return;
                }
            }
            CurrentToolMode = "copy";
            AfterChangeConfig();
        }

        internal void BeginEraseIn(byte layer) {
            if (The.Toolbox.ActiveTool == tintTool) {
                if (CurrentToolLayer == layer && CurrentToolMode == "erase") {
                    The.Toolbox.Clear();
                    return;
                }
            }
            CurrentToolLayer = layer;
            CurrentToolMode = "erase";
            AfterChangeConfig();
        }

        internal void BeginDrawIn(byte layer) {
            // No color selected — use first saved color or switch to eyedropper
            if (CurrentToolColor.a == 0) {
                CurrentToolLayer = layer;
                if (Arrays.IsEmpty(SavedColors)) {
                    CurrentToolMode = "copy";
                    AfterChangeConfig();
                    return;
                } else {
                    CurrentToolColor = SavedColors[0];
                }
            }
            if (The.Toolbox.ActiveTool == tintTool) {
                if (CurrentToolLayer == layer && CurrentToolMode == "draw") {
                    The.Toolbox.Clear();
                    return;
                }
            }
            CurrentToolLayer = layer;
            CurrentToolMode = "draw";
            AfterChangeConfig();
        }
    }
}
