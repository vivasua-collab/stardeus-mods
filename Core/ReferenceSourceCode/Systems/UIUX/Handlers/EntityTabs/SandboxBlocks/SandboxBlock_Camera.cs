using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.Systems.Creatures;
using Game.Systems.Items;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Text;
using KL.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public class SandboxBlock_Camera : SandboxBlock {
        public const string BlockId = "Camera";
        public override int Order => -100;
        public override string Id => BlockId;

        [SelfInit]
        private static void Register() => Register(new SandboxBlock_Camera());

        public override bool ShowFor(GameState s, BeingData being) => true;
        public override bool ShowFor(GameState s, Tile tile) => true;
        public override bool ShowFor(GameState s, ObjData obj) => true;

        private bool isFlyShowUI;
        private bool isShowIcons = true;
        private bool isShowTexts = true;
        private bool isForceParticles;
        private bool isCursorEffectsOn = true;
        private bool isCursorVisible = true;
        private int maxSlots = 3;
        private float flyDuration = 10f;

        private WButtonIconText2[] slotWidgets;
        private CameraMemory mem;

        private void EnsureMem() {
            mem ??= GameObject.FindAnyObjectByType<CameraMemory>();
        }

        public override void LoadContent(SandboxContext ctx, WGroup group, ref int gIdx) {
            EnsureMem();
            var lifetime = ctx.Lifetime;
            var S = ctx.S;

            // --- Visual tools block ---
            var block = UISpawn.Block(lifetime, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Camera Tools");

            if (CachedPlayerPrefs.GetInt(Res.TextureSizePref) > 0) {
                var warn = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                warn.Load(IconId.CWarning, Texts.Red("Low Texture Quality — switch Settings > Video > Texture Quality to Full"));
            }

#if !UNITY_STANDALONE_OSX
            if (!The.Prefs.GetBool(Pref.GNebulaOn, PrefH.GNebulaOn, Consts.DefaultNebulaOn)) {
                var warn = UISpawn.Widget<WIconText>(lifetime,
                    UIPrefabId.WContent2Text, block.Container, idx++);
                warn.Load(IconId.CWarning, "Space Nebula Disabled — enable in Settings > Video");
            }
#endif

            var logoBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            logoBtn.Load(IconId.CComputing, "Toggle Logo", null,
                () => The.SysSig.ToggleWatermark.Send());

            var hideUIBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            hideUIBtn.Load(IconId.WEye, "Hide UI Now", null,
                () => The.SysSig.ToggleUI.Send());

            var iconsToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            iconsToggle.Load("Toggle Icons", isShowIcons, isOn => {
                isShowIcons = S.Sys.Rend.ToggleIcons();
            });

            var textsToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            textsToggle.Load("Toggle Texts", isShowTexts, isOn => {
                isShowTexts = GameTextTMP.ToggleForceHide();
            });

            var particlesToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            particlesToggle.Load("Force All Particles", isForceParticles, isOn => {
                isForceParticles = S.Sys.Rend.ToggleForceParticles();
            });

            var flyUIToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            flyUIToggle.Load("UI While Executing", isFlyShowUI, isOn => {
                isFlyShowUI = isOn;
            });

            var cursorFxToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            cursorFxToggle.Load("Cursor Effects", isCursorEffectsOn, isOn => {
                isCursorEffectsOn = isOn;
                VirtualMouseInput.Instance.SetSuppressEffects(!isOn);
            });

            var cursorVisToggle = UISpawn.Widget<WToggleText>(lifetime,
                UIPrefabId.WContent2Toggle, block.Container, idx++);
            cursorVisToggle.Load("Cursor Visible", isCursorVisible, isOn => {
                isCursorVisible = isOn;
                VirtualMouseInput.Instance.SetSuppressVisibility(!isOn);
            });

            var fpsBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, block.Container, idx++);
            string FpsText() => Application.targetFrameRate < 1
                ? Screen.currentResolution.refreshRateRatio.value.ToString()
                : Application.targetFrameRate.ToString();
            fpsBtn.Load(IconId.CRobotEye, "FPS Cap", FpsText(), () => {
                if (Application.targetFrameRate < 0) {
                    Application.targetFrameRate = 0;
                }
                Application.targetFrameRate += 30;
                if (Application.targetFrameRate > 60) {
                    Application.targetFrameRate = -1;
                }
                fpsBtn.Load(IconId.CRobotEye, "FPS Cap", FpsText(), null);
            });

            // --- Flyover path block ---
            var flyBlock = UISpawn.Block(lifetime, group, gIdx++);
            int fIdx = 0;

            var flyHeader = UISpawn.Widget<WText>(lifetime,
                UIPrefabId.WContent2Header, flyBlock.Container, fIdx++);
            flyHeader.Load("Camera Fly Over Path");

            var durationSlider = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, flyBlock.Container, fIdx++);
            durationSlider.Item.Load(IconId.CHaul, "Duration", $"{flyDuration:0.#}s");
            durationSlider.Slider.Load(flyDuration, v => { flyDuration = (float)v; }, 1f, 60f);
            durationSlider.Slider.SetValueTextFunc(v => $"{v:0.#}s");

            var slotsSlider = UISpawn.Widget<WSliderWithIconText2>(lifetime,
                UIPrefabId.WContent2Text2Slider, flyBlock.Container, fIdx++);
            slotsSlider.Item.Load(IconId.CCount, "Path Points", Units.XOutOfY(maxSlots, 20));
            slotsSlider.Slider.Load(maxSlots, v => {
                maxSlots = Mathf.RoundToInt((float)v);
                slotsSlider.Item.SetText2(Units.XOutOfY(maxSlots, 20));
            }, 2f, 20f);
            slotsSlider.Slider.SetValueTextFunc(v => Units.XOutOfY(Mathf.RoundToInt((float)v), 20));

            var previewBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, flyBlock.Container, fIdx++);
            previewBtn.Load(IconId.CTarget, "Preview Path", null,
                () => FlyThrough(S, true, false, 0f));

            var execBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, flyBlock.Container, fIdx++);
            execBtn.Load(IconId.CTarget, "Execute Path", null,
                () => FlyThrough(S, isFlyShowUI, true, 1f));

            // Slot buttons
            slotWidgets = new WButtonIconText2[maxSlots];
            for (int i = 0; i < maxSlots; i++) {
                var slotNum = i;
                var slotBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                    UIPrefabId.WContent2Button, flyBlock.Container, fIdx++);
                slotBtn.Load(IconId.CRobotEye,
                    mem != null ? mem.GetSlot(i) : $"[{i}] Empty",
                    "Save",
                    () => mem?.SaveSlot(slotNum));
                UIUtils.AddTip(lifetime, slotBtn,
                    $"Ctrl+Numpad {slotNum} to Save, Numpad {slotNum} to Jump");
                slotWidgets[i] = slotBtn;
            }

            if (mem != null) {
                mem.AfterChange = UpdateSlots;
            }
            lifetime.OnDispose(() => {
                if (mem != null && mem.AfterChange == UpdateSlots) {
                    mem.AfterChange = null;
                }
                slotWidgets = null;
            });

            var clearBtn = UISpawn.Widget<WButtonIconText2>(lifetime,
                UIPrefabId.WContent2Button, flyBlock.Container, fIdx++);
            clearBtn.Load(IconId.CDanger, "Delete All Points", null, () => {
                WPopup.Create(false, IconId.CDanger, "Delete All Points",
                    "Delete all camera path points?")
                    .WithAction(T.Confirm, () => {
                        if (mem == null) { return; }
                        for (int i = 0; i < 20; i++) {
                            mem.DeleteSlot(i);
                        }
                    })
                    .WithAction2(T.Cancel, null)
                    .Spawn();
            });
        }

        private void UpdateSlots() {
            if (slotWidgets == null || mem == null) { return; }
            for (int i = 0; i < slotWidgets.Length; i++) {
                var w = slotWidgets[i];
                if (w == null) { continue; }
                w.Load(IconId.CRobotEye, mem.GetSlot(i), "Save", () => mem.SaveSlot(i));
            }
        }

        private void FlyThrough(GameState S, bool showUI, bool unpause, float delay) {
            if (isFlyingThoughNow) {
                isFlyingThoughNow = false;
                UISounds.PlayCancel();
                return;
            }
            if (unpause && G.ClockSpeed == ClockSpeed.Stopped) {
                S.Clock.Toggle();
            }
            Coroutines.Start(DoFlyThrough(S, mem, showUI, maxSlots, flyDuration, delay),
                this, "DevFlyThrough", false, false);
        }

        public static void StartDemoFlyThrough() {
            stopDemoFlyThrough = false;
            A.S.Clock.SetSpeed(ClockSpeed.Normal);
            Coroutines.Start(AutoDemo(A.S), "Demo", "DemoFlyThrough", false, false);
        }

        public static void StopDemoFlyThrough() {
            stopDemoFlyThrough = true;
            var ctrl = GameObject.FindAnyObjectByType<CameraControls>();
            if (ctrl == null) { return; }
            ctrl.IgnoreInput = false;
            ctrl.WantPosition = ctrl.PrecisePosition;
            ctrl.WantOrtho = ctrl.PreciseOrtho;
        }

        private const float AutoDemoZoomOutDuration = 5f;
        private const float AutoDemoZoomOutDistance = 30f;
        private static bool stopDemoFlyThrough;
        private static bool isFlyingThoughNow;

        private static IEnumerator AutoDemo(GameState S) {
            var ctrl = GameObject.FindAnyObjectByType<CameraControls>();
            ctrl.IgnoreInput = true;
            var startPosition = ctrl.PrecisePosition;
            var startZoom = ctrl.PreciseOrtho;
            var wasDemoMode = G.IsDemoMode;

            var pointList = new List<Vector2>();
            Curve zCurve = new float[] {
                0f, 4f,
                0.5f, 8f,
                0.75f, 20f,
                1f, AutoDemoZoomOutDistance,
            };

            foreach (var tile in S.DataGrids[WorldLayer.Objects].Data) {
                if (tile != null && !tile.Transform.IsChild && tile.IsConstructed) {
                    if (EntityUtils.IsWithinMargin(tile.PosIdx, false, false)) {
                        pointList.Add(tile.Position);
                    }
                }
            }

            var rng = new Rng((int)DateTimeOffset.Now.ToUnixTimeSeconds());
            var fnoise = new FastNoise(rng.Int);

            var pointsList2 = pointList.OrderBy(t => rng.Value).Take(30).Select(n => {
                var n2 = n * 10f;
                var noise = fnoise.GetSimplex(n2.x, n2.y) * 0.5f + 0.5f;
                var z = zCurve.Evaluate(noise);
                return new Vector3(n.x, n.y, z);
            }).ToList();

            var pointsNew = new List<Vector3>();
            float dist = 0f;
            Vector3? prevPoint = null;
            foreach (var pnt in pointsList2) {
                if (Rng.UChance(0.5f)) {
                    var pntPre = pnt + (Vector3)(Rng.UInsideUnitCircle() * 5f);
                    pointsNew.Add(pntPre);
                    pointsNew.Add(pnt);
                    var pntPost = pnt + (Vector3)(Rng.UInsideUnitCircle() * 5f);
                    pointsNew.Add(pntPost);
                } else {
                    pointsNew.Add(pnt);
                }
                if (prevPoint.HasValue) {
                    dist += Vector3.Distance(prevPoint.Value, pnt);
                }
                prevPoint = pnt;
            }
            var autoDuration = dist * 0.25f;
            D.Warn("Dist: {0}, Duration: {1}", dist, autoDuration);
            if (autoDuration < 300f) {
                autoDuration = 300f;
            }

            // var zoomOutDistance = Mathf.Max(startZoom, AutoDemoZoomOutDistance);
            // var zoomOutElapsed = 0f;
            // while (zoomOutElapsed < AutoDemoZoomOutDuration) {
            //     yield return null;
            //     if (ShouldStopAutoDemo(S, wasDemoMode)) { break; }
            //     zoomOutElapsed += GameTimer.unscaledDeltaTime;
            //     var t = Mathf.SmoothStep(0f, 0.5f,
            //         zoomOutElapsed / AutoDemoZoomOutDuration);
            //     ctrl.SetZoomDistance(Mathf.Lerp(startZoom, zoomOutDistance, t));
            // }

            var zoomOutDistance = startZoom;

            pointsNew.Insert(0, new Vector3(startPosition.x, startPosition.y,
                zoomOutDistance));
            var points = pointsNew.ToArray();

            // Nothing constructed to fly over (a fresh New Beginning wreck): a one-point curve
            // logged an error every frame
            while (points.Length > 1) {
                var startT = Time.realtimeSinceStartupAsDouble;
                var curT = startT;
                var endT = startT + autoDuration;

                while (curT < endT) {
                    yield return null;
                    if (ShouldStopAutoDemo(S, wasDemoMode)) { break; }
                    curT += GameTimer.unscaledDeltaTime;
                    var t = Maths.InverseLerp(startT, endT, curT);
                    var point = Bezier3.Curve(t, points);
                    ctrl.TeleportTo(new Vector3(point.x, point.y, ctrl.PrecisePosition.z));
                    ctrl.SetZoomDistance(point.z);
                }

                if (ShouldStopAutoDemo(S, wasDemoMode)) { break; }
            }

            ctrl.WantPosition = ctrl.PrecisePosition;
            ctrl.WantOrtho = ctrl.PreciseOrtho;
            ctrl.IgnoreInput = false;
            isFlyingThoughNow = false;
        }

        private static bool ShouldStopAutoDemo(GameState S, bool wasDemoMode) {
            return stopDemoFlyThrough
                || !GameState.IsCurrent(S)
                || (wasDemoMode ? !G.IsDemoMode : G.HasEnteredGame);
        }

        private static IEnumerator DoFlyThrough(GameState S, CameraMemory mem, bool showUI,
                int maxSlots, float flyDuration, float delay) {
            isFlyingThoughNow = true;
            bool restoreUI = false;
            if (!G.IsDemoMode && showUI == ToggleUI.IsUIHidden) {
                restoreUI = true;
                The.SysSig.ToggleUI.Send();
            }
            mem.TryJumpTo(0);
            if (delay > 0) {
                yield return S.WaitForSecondsGameTime(delay);
                if (!GameState.IsCurrent(S)) { yield break; }
            }

            var ctrl = mem.CamControls;
            ctrl.IgnoreInput = true;

            var pointList = new List<Vector3>();
            int slotNum = 0;
            while (slotNum < maxSlots) {
                if (mem.TryGetSlot(slotNum++, out var x, out var y, out var z)) {
                    pointList.Add(new(x, y, z));
                } else {
                    break;
                }
            }

            if (pointList.Count == 0) {
                ctrl.IgnoreInput = false;
                isFlyingThoughNow = false;
                yield break;
            }

            var points = pointList.ToArray();
            var kc = Keyboard.current;
            var wasDemoMode = G.IsDemoMode;

            while (true) {
                var startT = Time.realtimeSinceStartupAsDouble;
                var curT = startT;
                var endT = startT + flyDuration;

                while (curT < endT) {
                    yield return null;
                    if (!wasDemoMode) {
                        if (!isFlyingThoughNow || kc?.backspaceKey.isPressed == true
                                || kc?.escapeKey.isPressed == true) {
                            break;
                        }
                    }
                    curT += GameTimer.unscaledDeltaTime;
                    var t = Maths.InverseLerp(startT, endT, curT);
                    var point = Bezier3.Curve(t, points);
                    ctrl.TeleportTo(new Vector3(point.x, point.y, ctrl.PrecisePosition.z));
                    ctrl.SetZoomDistance(point.z);
                    if (wasDemoMode && !G.IsDemoMode) { break; }
                }

                ctrl.WantPosition = ctrl.PrecisePosition;
                ctrl.WantOrtho = ctrl.PreciseOrtho;
                ctrl.IgnoreInput = false;
                if (!G.IsDemoMode || !GameState.IsCurrent(S)) { break; }
            }

            if (!wasDemoMode) {
                if (delay > 0) {
                    yield return new WaitForSeconds(delay);
                }
                if (restoreUI) {
                    The.SysSig.ToggleUI.Send();
                }
            }

            isFlyingThoughNow = false;
        }
    }
}
