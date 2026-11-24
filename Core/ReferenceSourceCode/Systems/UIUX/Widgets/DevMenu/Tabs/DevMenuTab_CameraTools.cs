#if !STARDEUS_DEMO
using System;
using System.Collections;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Rendering;
using Game.UI;
using Game.Utils;
using KL.Randomness;
using KL.Text;
using KL.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public sealed class DevMenuTab_CameraTools : DevMenuTab {
        public const string TabIdConst = "CameraTools";
        public override string TabId => TabIdConst;
        public override string TabName => "Camera Tools";

        private bool isShowIcons = true;
        private bool isShowTexts = true;
        private bool isForceParticles;
        private bool isCursorEffectsOn = true;
        private bool isCursorVisible = true;
        private bool isFlyShowUI;
        private float flyDuration = 10f;
        private int maxSlots = 3;
        private static bool isFlyingThoughNow;

        private readonly CameraMemory mem;

        public DevMenuTab_CameraTools(GameState s) : base(s) {
            mem = GameObject.FindFirstObjectByType<CameraMemory>();
        }

        public override void LoadContent(Lifetime life, WGroup group, ref int gIdx,
                SubPanelWidget subPanel, Action reload) {
            if (CachedPlayerPrefs.GetInt(Res.TextureSizePref) > 0) {
                var warnBlock = UISpawn.Block(life, group, gIdx++);
                int wi = 0;
                var warnHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, warnBlock.Container, wi++);
                warnHeader.Load(Texts.Red("Low Texture Quality!"));
                var warnText = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, warnBlock.Container, wi++);
                warnText.Load("Game is running with non-full texture quality. Switch Settings > Video > Texture Quality to Full and restart to unlock capture tools.");
                #if !UNITY_EDITOR
                return;
                #endif
            }

            #if !UNITY_STANDALONE_OSX
            if (!The.Prefs.GetBool(Pref.GNebulaOn, PrefH.GNebulaOn, Consts.DefaultNebulaOn)) {
                var warnBlock = UISpawn.Block(life, group, gIdx++);
                int wi = 0;
                var warnHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, warnBlock.Container, wi++);
                warnHeader.Load("Space Nebula Disabled!");
                var warnText = UISpawn.Widget<WText>(life, UIPrefabId.WContent2TextBlock, warnBlock.Container, wi++);
                warnText.Load("Please check Settings > Video > Display Space Nebula to unlock capture tools.");
                #if !UNITY_EDITOR
                return;
                #endif
            }
            #endif

            var block = UISpawn.Block(life, group, gIdx++);
            int idx = 0;

            var header = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, block.Container, idx++);
            header.Load("Camera Tools");

            // Toggle Logo
            var logoBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            logoBtn.Load(IconId.CComputing, "Toggle Logo", "Toggle", () => The.SysSig.ToggleWatermark.Send());

            // Hide UI
            var hideUIBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            hideUIBtn.Load(IconId.WEye, "Hide UI Now", "Hide",
                () => {
                    The.SysSig.ToggleUI.Send();
                    S.UIQuery.ToggleDevMenu.Ask(false);
                });
            UIUtils.AddTip(life, hideUIBtn, "Escape / Cancel will unhide");

            // Toggle Icons
            var iconsBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            iconsBtn.Load(isShowIcons ? IconId.CCheck : IconId.CCross,
                "Toggle Icons", "Toggle",
                () => {
                    isShowIcons = S.Sys.Rend.ToggleIcons();
                    reload();
                });
            UIUtils.AddTip(life, iconsBtn, "Toggles status icons, progress bars, thought bubbles");

            // Toggle Texts
            var textsBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            textsBtn.Load(isShowTexts ? IconId.CCheck : IconId.CCross,
                "Toggle Texts", "Toggle",
                () => {
                    isShowTexts = GameTextTMP.ToggleForceHide();
                    reload();
                });
            UIUtils.AddTip(life, textsBtn, "Toggles names and other in-game texts. May need to unpause to apply.");

            // Force Particles
            var particlesBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            particlesBtn.Load(isForceParticles ? IconId.CCheck : IconId.CCross,
                "Force All Particles", "Toggle",
                () => {
                    isForceParticles = S.Sys.Rend.ToggleForceParticles();
                    reload();
                });
            UIUtils.AddTip(life, particlesBtn, "Forces every device to show particles as if operating. Unpause to see effect.");

            // Cursor Effects
            var cursorEffectsBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            cursorEffectsBtn.Load(isCursorEffectsOn ? IconId.CCheck : IconId.CCross,
                "Cursor Effects", "Toggle",
                () => {
                    isCursorEffectsOn = !isCursorEffectsOn;
                    VirtualMouseInput.Instance.SetSuppressEffects(!isCursorEffectsOn);
                    reload();
                });

            // Cursor Visible
            var cursorVisBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            cursorVisBtn.Load(isCursorVisible ? IconId.CCheck : IconId.CCross,
                "Cursor Visible", "Toggle",
                () => {
                    isCursorVisible = !isCursorVisible;
                    VirtualMouseInput.Instance.SetSuppressVisibility(!isCursorVisible);
                    reload();
                });

            // FPS Cap
            var fpsRow = UISpawn.Widget<WIconText2>(life, UIPrefabId.WContent2Text2, block.Container, idx++);
            string fpsText = Application.targetFrameRate < 1
                ? Screen.currentResolution.refreshRateRatio.value.ToString()
                : Application.targetFrameRate.ToString();
            fpsRow.Load(IconId.CRobotEye, "FPS Cap", fpsText);

            var fpsBtn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WContent2ButtonSimple, block.Container, idx++);
            fpsBtn.Load("Toggle FPS Cap", () => {
                if (Application.targetFrameRate < 0) { Application.targetFrameRate = 0; }
                Application.targetFrameRate += 30;
                if (Application.targetFrameRate > 60) { Application.targetFrameRate = -1; }
                reload();
            });

            // UI During Flyover
            var flyUIBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, block.Container, idx++);
            flyUIBtn.Load(isFlyShowUI ? IconId.CCheck : IconId.CCross,
                "UI While Executing Flyover", "Toggle",
                () => {
                    isFlyShowUI = !isFlyShowUI;
                    reload();
                });

            // --- Camera Fly-Over Path section ---
            var flyBlock = UISpawn.Block(life, group, gIdx++);
            int fIdx = 0;

            var flyHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, flyBlock.Container, fIdx++);
            flyHeader.Load("Camera Fly-Over Path");

            // Slot count (priority up/down)
            var slotCount = UISpawn.Widget<WPriorityUpDown>(life, UIPrefabId.WContent4Priority, flyBlock.Container, fIdx++);
            slotCount.Up.Load(IconId.WPlus, () => { maxSlots = Mathf.Clamp(maxSlots + 1, 2, 20); reload(); });
            slotCount.Down.Load(IconId.WMinus, () => { maxSlots = Mathf.Clamp(maxSlots - 1, 2, 20); reload(); });
            slotCount.Texts.Load("Path Points", Units.XOutOfY(maxSlots, 20));
            UIUtils.AddTip(life, slotCount, "Add or remove path points. Removing points won't delete their memory.");

            // Fly Duration Slider
            var durSlider = UISpawn.Widget<WSliderWithIconText2>(life, UIPrefabId.WContent2Text2Slider, flyBlock.Container, fIdx++);
            durSlider.Item.Load(IconId.CHaul, "Duration", $"{flyDuration:0.#}s");
            durSlider.Slider.Load(flyDuration, v => { flyDuration = v; }, 1f, 60f);
            durSlider.Slider.SetValueTextFunc(v => $"{v:0.#}s");
            UIUtils.AddTip(life, durSlider, "Flight duration in seconds");

            // Preview Path
            var previewBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, flyBlock.Container, fIdx++);
            previewBtn.Load(IconId.CTarget, "Preview Path", "Preview",
                () => FlyThrough(showUI: true, unpause: false, delay: 0f));
            UIUtils.AddTip(life, previewBtn, "Preview camera flight path without unpausing");

            // Execute Path
            var executeBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, flyBlock.Container, fIdx++);
            executeBtn.Load(IconId.CTarget, "Execute Path", "Execute", () => {
                S.UIQuery.ToggleDevMenu.Ask(false);
                FlyThrough(isFlyShowUI, unpause: true, delay: 1f);
            });
            UIUtils.AddTip(life, executeBtn, "Closes this panel, unpauses and flies though points. Escape/Backspace cancels.");

            // Camera slots
            var slotsBlock = UISpawn.Block(life, group, gIdx++);
            int sIdx = 0;

            var slotsHeader = UISpawn.Widget<WText>(life, UIPrefabId.WContent2Header, slotsBlock.Container, sIdx++);
            slotsHeader.Load("Path Point Slots");

            float aspect = RenderUtils.Camera.aspect;
            for (int i = 0; i < maxSlots; i++) {
                int slotNum = i;
                string slotTitle = mem != null ? mem.GetSlot(i) : $"Slot {i}";
                var slotBtn = UISpawn.Widget<WButtonIconText2>(life, UIPrefabId.WContent2Button, slotsBlock.Container, sIdx++);
                slotBtn.Load(IconId.CRobotEye, slotTitle, $"Save #{i}", () => {
                    mem?.SaveSlot(slotNum);
                    reload();
                });
                UIUtils.AddTip(life, slotBtn, $"Click to save slot #{i}. Icon to jump. Numpad {i} to jump, Ctrl+Numpad {i} to save.");
                UIUtils.AddHover(life, slotBtn, on => {
                    if (on) { mem?.TryJumpTo(slotNum); }
                });
            }

            // Delete All Points
            var deleteBlock = UISpawn.Block(life, group, gIdx++);
            int dIdx = 0;
            var deleteBtn = UISpawn.Widget<WButtonText>(life, UIPrefabId.WPanelButton_Red, deleteBlock.Container, dIdx++);
            deleteBtn.Load("Delete All Points (Long Press)", () => {
                for (int i = 0; i < 20; i++) { mem?.DeleteSlot(i); }
                reload();
            });
        }

        private void FlyThrough(bool showUI, bool unpause, float delay) {
            if (isFlyingThoughNow) {
                isFlyingThoughNow = false;
                UISounds.PlayCancel();
                return;
            }
            if (unpause && G.ClockSpeed == ClockSpeed.Stopped) {
                S.Clock.Toggle();
            }
            Coroutines.Start(DoFlyThrough(S, mem, showUI, maxSlots, flyDuration, delay), this, "DevFlyThrough", false, false);
        }

        private static IEnumerator DoFlyThrough(GameState S, CameraMemory mem, bool showUI, int maxSlots, float flyDuration, float delay) {
            isFlyingThoughNow = true;
            bool restoreUI = false;
            if (!G.IsDemoMode && showUI == ToggleUI.IsUIHidden) {
                restoreUI = true;
                The.SysSig.ToggleUI.Send();
            }
            mem?.TryJumpTo(0);
            if (delay > 0) {
                yield return S.WaitForSecondsGameTime(delay);
                if (!GameState.IsCurrent(S)) { yield break; }
            }
            var ctrl = mem?.CamControls;
            if (ctrl == null) { isFlyingThoughNow = false; yield break; }
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

            while (true) {
                var startT = Time.realtimeSinceStartupAsDouble;
                var curT = startT;
                var endT = startT + flyDuration;
                while (curT < endT) {
                    yield return null;
                    if (!isFlyingThoughNow || kc?.backspaceKey.isPressed == true || kc?.escapeKey.isPressed == true) { break; }
                    curT += GameTimer.unscaledDeltaTime;
                    var t = Maths.InverseLerp(startT, endT, curT);
                    var point = Bezier3.Curve(t, points);
                    ctrl.TeleportTo(new Vector3(point.x, point.y, ctrl.PrecisePosition.z));
                    ctrl.SetZoomDistance(point.z);
                }
                ctrl.WantPosition = ctrl.PrecisePosition;
                ctrl.WantOrtho = ctrl.PreciseOrtho;
                ctrl.IgnoreInput = false;
                break;
            }
            if (delay > 0) {
                yield return new WaitForSeconds(delay);
            }
            if (restoreUI) {
                The.SysSig.ToggleUI.Send();
            }
            isFlyingThoughNow = false;
        }
    }
}
#endif
