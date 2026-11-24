using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Platform;
using Game.UI;
using Game.Utils;
using KL.UI;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageInput : BaseSettingsPage {
        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageInput());
        }

        public const string PageId = "Input";
        public override string Id => PageId;
        public override int Order => 40;
        public override string TabName => T.MenuSettingsInput;

        private bool isRestartRequired;

        private string gamepadFlavor => VirtualMouseInput.GamepadFlavor;
        private Gamepad lastGamepad;

        private WText2 gamepadDeadzoneRangeLabel;

        private WDropdownWithLabel escDrop;
        private WDropdownWithLabel rmbDrop;
        // A getter: pages register before the language loads, so a list built then stays English
        private static List<string> EscBehaviorOptions => new() {
            T.EscBehaviorOpenPausePanel,
            T.EscBehaviorBoth,
            T.EscBehaviorCloseMenus,
        };

        private readonly Dictionary<string, WInputRebind> rebindButtons = new();

        protected override void LoadTab() {
            AddFooterButton(0, T.ResetToDefaults, ResetToDefaults, T.ResetInputBindingsTip);
            if (isRestartRequired) {
                var restartBtn = AddFooterButton(1, Texts.Bold(T.RestartRequired),
                    () => PlatformUtils.SuggestRestart(false), T.RestartRequiredInfo);
                restartBtn.OverrideButtonTheme("Red");
            }
            ShowFooter(true);

            int idx = 0;

            idx = AddMainBlock(idx);
            idx = AddGamepadsBlock(idx);

            CreateControls(idx);

            lastGamepad = Gamepad.current;
        }

        private int AddGamepadsBlock(int idx) {
            AddHeader(idx++, T.InputGamepad);

            var ignoreGamepads = !PlatformUtils.UseGamepad();
            if (ignoreGamepads) {
                // Gamepad Ignore
                var gamepadIgnoreToggle = AddToggle(idx++, T.GamepadIgnore,
                        !PlatformUtils.UseGamepad(), (v) => {
                    SetGBool(Pref.GGamepadIgnore, v);
                    isRestartRequired = true;
                    PlatformUtils.SuggestRestart();
                    ReloadTab();
                });
                AddGamepadIgnoreTooltip(gamepadIgnoreToggle);
                return idx;
            }

            // Gamepad Ignore / Gamepad Rumble
            var gamepadIgnoreRumble = AddTogglePair(idx++, T.GamepadIgnore, !PlatformUtils.UseGamepad(), (v) => {
                SetGBool(Pref.GGamepadIgnore, v);
                isRestartRequired = true;
                PlatformUtils.SuggestRestart();
                ReloadTab();
            }, null,
            T.GamepadRumble, GBool(Pref.GGamepadRumble, true), (v) => {
                SetGBool(Pref.GGamepadRumble, v);
                The.SysSig.GamepadRumble.Send(0.2f, 0.2f, 0.1f);
            });
            AddGamepadIgnoreTooltip(gamepadIgnoreRumble.Toggle1);


            #if !DISABLESTEAMWORKS
            // Disable virtual keyboard
            if (The.Platform.IsConsole()) {
                var disableVKey = AddToggle(idx++, T.DisableSteamVirtualKeyboard,
                        GBool(Pref.GDisableVirtualKeyboard, false), (v) => {
                    SetGBool(Pref.GDisableVirtualKeyboard, v);
                });
            }
            #endif

            // Gamepad deadzone
            var dzMin = GFloat(Pref.GDeadZoneMin, Consts.DefaultDeadZoneMin);
            var dzMax = GFloat(Pref.GDeadZoneMax, Consts.DefaultDeadZoneMax);
            var dzMinMax = Consts.DefaultDeadZoneMinMax;
            var dzMaxMin = Consts.DefaultDeadZoneMaxMin;
            var dzMinMin = Consts.DefaultDeadZoneMinMin;
            var dzMaxMax = Consts.DefaultDeadZoneMax;
            // For deadzone, perhaps add two sliders
            // AddHeader(idx++, T.GamepadDeadZone_V2.F(dzMin, dzMax));

            // Min deadzone slider
            var deadzoneRange = AddRangeSlider(idx++, T.SettingGamepadDeadZone, dzMin, dzMax,
                dzMinMin, dzMinMax, dzMaxMin, dzMaxMax,
            (v1, v2) => {
                SetGFloat(Pref.GDeadZoneMin, v1);
                SetGFloat(Pref.GDeadZoneMax, v2);
                UpdateDeadzone();
            });
            gamepadDeadzoneRangeLabel = deadzoneRange.Label;
            UpdateDeadzoneRangeLabel(dzMin, dzMax);

            // VMouse Sensitivity
            var vMouseSens = GFloat(Pref.GVMouseSensitivity, Consts.DefaultVMouseSens);
            var vMouseSensSlider = AddSliderWithRange(idx++, T.SettingGamepadPointerSensitivity, vMouseSens,
                    Consts.VMouseSensMin, Consts.VMouseSensMax, (v) => {
                SetGFloat(Pref.GVMouseSensitivity, v);
            }, T.VMouseSensTip);
            vMouseSensSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GVMouseSensitivity, Consts.DefaultVMouseSens).ToString("0"));

            // VMouse Accel
            var vMouseAccel = GFloat(Pref.GVMouseAccel, Consts.DefaultVMouseAccel);
            var vMouseAccelSlider = AddSliderWithRange(idx++, T.SettingGamepadPointerAcceleration,
                vMouseAccel, 0f, Consts.VMouseAccelMax, (v) => {
                var newVal = Maths.ClampSnap(v, 0f, Consts.VMouseAccelMax, 0.1f);
                SetGFloat(Pref.GVMouseAccel, newVal);
            }, T.VmouseAccelerationTip);
            vMouseAccelSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GVMouseAccel, Consts.DefaultVMouseAccel).ToString("0.0"));

            // VMouse MaxSpeed
            var vMouseMaxSpeed = GFloat(Pref.GVMouseMaxSpeed, Consts.DefaultVMouseMaxSpeed);
            var vMouseMaxSpeedSlider = AddSliderWithRange(idx++, T.SettingGamepadPointerMaxSpeed,
                   vMouseMaxSpeed, Consts.VMouseMaxSpeedMin, Consts.VMouseMaxSpeedMax, (v) => {
                var newVal = Maths.ClampSnap(v, Consts.VMouseMaxSpeedMin, Consts.VMouseMaxSpeedMax, 0.1f);
                SetGFloat(Pref.GVMouseMaxSpeed, newVal);
            }, T.VmouseMaxSpeedTip);
            vMouseMaxSpeedSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GVMouseMaxSpeed, Consts.DefaultVMouseMaxSpeed).ToString("0.0"));
            return idx;
        }

        private int AddMainBlock(int idx) {
            // AddHeader(idx++, T.InputMouse);

            // Use software cursor / Smooth Drag
            AddTogglePair(idx++, T.CursorSoftware, GBool(Pref.GSoftwareCursor, Video.CursorSoftDefault), (v) => {
                SetGBool(Pref.GSoftwareCursor, v);
                The.SysSig.SetCursor.Send(UIPoolRoot.LastCursor);
            }, T.SoftCursorTip,
            T.SmoothDrag, GBool(Pref.GSmoothDrag, Consts.DefaultSmoothDrag), (v) => {
                SetGBool(Pref.GSmoothDrag, v);
            }, T.SmoothDragTip);

            // Invert UI Scroll
            AddTogglePair(idx++, T.UiScrollInvert, GBool(Pref.GUIScrollInvert, Consts.DefaultUIScrollInverted), (v) => {
                SetGBool(Pref.GUIScrollInvert, v);
            }, T.InvertScrollTip,
            T.SmoothZoom, GBool(Pref.GSmoothZoom, Consts.DefaultSmoothZoom), (v) => {
                SetGBool(Pref.GSmoothZoom, v);
            });

            // UI Scroll speed
            var scrollSpeed = GFloat(Pref.GUIScrollSpeed, Consts.DefaultUIScrollSpeed);
            var scrollSpeedSlider = AddSliderWithRange(idx++, T.SettingUiScrollSpeed, scrollSpeed, 1f, 10f, (v) => {
                var newVal = Maths.ClampSnap(v, 1f, 10f, 0.25f);
                SetGFloat(Pref.GUIScrollSpeed, newVal);
            }, T.ScrollSpeedTip);
            scrollSpeedSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GUIScrollSpeed, Consts.DefaultUIScrollSpeed).ToString("0.00"));

            // Zoom sensitivity
            var zoomSens = GFloat(Pref.GZoomSensitivity, Consts.DefaultZoomSensitivity);
            var zoomSensSlider = AddSliderWithRange(idx++, T.SettingZoomSensitivity, zoomSens, 1f, 20f, (v) => {
                var newVal = Maths.ClampSnap(v, 1f, 20f, 0.25f);
                SetGFloat(Pref.GZoomSensitivity, newVal);
            }, T.ZoomSensTip);
            zoomSensSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GZoomSensitivity, Consts.DefaultZoomSensitivity).ToString("0.00"));

            // Pan sensitivity
            var panSens = GFloat(Pref.GPanSensitivity, Consts.DefaultPanSensitivity);
            var panSensSlider = AddSliderWithRange(idx++, T.SettingPanSensitivity, panSens, 1f, 30f, (v) => {
                var newVal = Mathf.Round(v);
                SetGFloat(Pref.GPanSensitivity, newVal);
            }, T.PanSensTip);
            panSensSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GPanSensitivity, Consts.DefaultPanSensitivity).ToString("0"));

            // Edge pan amount
            var edgePanAmt = GFloat(Pref.GEdgePanSpeed, Consts.DefaultEdgePanSpeed);
            var edgePanSlider = AddSliderWithRange(idx++, T.SettingEdgePanSpeed, edgePanAmt, 0f, 10f, (v) => {
                var newVal = Mathf.Round(v);
                SetGFloat(Pref.GEdgePanSpeed, newVal);
            }, T.EdgePanTip);
            edgePanSlider.Slider.SetValueTextFunc(
                (v) => GFloat(Pref.GEdgePanSpeed, Consts.DefaultEdgePanSpeed).ToString("0"));

            // Drag distance pixels
            var dist = GInt(Pref.GCancelDragDistancePixels, Consts.DefaultCancelDragDistancePx);
            var dragDistPixelsSlider = AddSliderWithRange(idx++, T.SettingIgnoredDragDist, dist, 1, 300, (v) => {
                int newVal = Mathf.RoundToInt(v);
                SetGInt(Pref.GCancelDragDistancePixels, newVal);
            }, T.DragDistTip);
            dragDistPixelsSlider.Slider.SetValueTextFunc(
                    (v) => T.NumPixels_V1.F(Mathf.RoundToInt(v)));

            // // RMB Behavior
            // var rmbGlyph = The.Bindings.GetBinding(ActionType.ClickCancel).AllControlGlyphs(out var _);
            // var selectedRmbBehavior = GInt(Pref.GRMBBehavior, 0);
            // rmbDrop = AddDropdown(idx++, T.RmbBehavior_V1.F(rmbGlyph),
            //     rmbBehaviorOptions, selectedRmbBehavior, OnRmbBehaviorChange, T.RmbBehaviorTooltip_V1.F(rmbGlyph));

            // Esc Behavior
            if (!VirtualMouseInput.IsCurrent) {
                var selectedEscBehavior = GInt(Pref.GESCBehavior, Consts.DefaultEscBehavior);
                var escGlyph = GlyphMap.Sprite("Keyboard", "escape");
                escDrop = AddDropdown(idx++, T.EscBehavior_V1.F(escGlyph), EscBehaviorOptions, selectedEscBehavior, OnEscBehaviorChange, T.EscBehaviorTooltip_V1.F(escGlyph));
            }


            return idx;
        }

        private void UpdateDeadzoneRangeLabel(float dzMin, float dzMax) {
            gamepadDeadzoneRangeLabel.SetText2(
                string.Format("{0:0.00}-{1:0.00}", dzMin, dzMax));
        }

        private void UpdateDeadzone() {
            var v1 = GFloat(Pref.GDeadZoneMin, Consts.DefaultDeadZoneMin);
            var v2 = GFloat(Pref.GDeadZoneMax, Consts.DefaultDeadZoneMax);
            // Update header or something, but since it's static, perhaps not needed
            The.Bindings.SetDeadZone(v1, v2);
            UpdateDeadzoneRangeLabel(v1, v2);
        }

        private void AddGamepadIgnoreTooltip(WToggleText gamepadToggle) {
            if (gamepadToggle == null) { return; }
            var sb = new StringBuilder(T.GamepadIgnoreTip);
            sb.AppendFormat("\n\nConnected input devices:");
            foreach (var dev in InputSystem.devices) {
                if (dev.enabled && dev != VirtualMouseInput.VMouse) {
                    sb.AppendFormat("\n - <b>{0}</b>", dev.displayName);
                    if (dev.noisy) {
                        sb.Append(" [noisy!]");
                    }
                }
            }
            AddTip(gamepadToggle, sb.ToString());
        }

        private void OnBindingChanged(ActionType action) {
            foreach (var row in rebindButtons.Values) {
                row.RefreshConflicts();
            }
        }

        private void CreateControls(int idx) {
            rebindButtons.Clear();
            // ReloadTab gets here again without passing BeforeUnloadTab first
            The.SysSig.BindingChanged.RemoveListener(OnBindingChanged);
            The.SysSig.BindingChanged.AddListener(OnBindingChanged);
            foreach (var g in The.Bindings.ByGroup) {
                if (g.Value.Count == 0) { continue; }
                AddHeader(idx++, $"input.group.{g.Key}".T());

                foreach (var b in g.Value) {
                    AddBinding(idx++, b);
                }
            }
        }

        private void AddBinding(int idx, MultiBinding b) {
            if (b.IsHidden) { return; }
            var btn = UISpawn.Widget<WInputRebind>(lifetime, UIPrefabId.WTabPanelItem_InputRebind, contentRect, idx);
            btn.Load(b, ReloadTab);
            UIUtils.AddTipFunc(lifetime, btn, () => The.Bindings.DescribeRelations(b, out _));
            rebindButtons.Add(b.Id, btn);
        }

        private void OnEscBehaviorChange(int opt) {
            SetGInt(Pref.GESCBehavior, opt);
        }

        private void ResetToDefaults() {
            SetGBool(Pref.GUIScrollInvert, false);
            SetGBool(Pref.GGamepadRumble, true);
            SetGBool(Pref.GSoftwareCursor, Video.CursorSoftDefault);
            SetGFloat(Pref.GUIScrollSpeed, Consts.DefaultUIScrollSpeed);
            SetGBool(Pref.GUIScrollInvert, Consts.DefaultUIScrollInverted);
            SetGFloat(Pref.GZoomSensitivity, Consts.DefaultZoomSensitivity);
            SetGFloat(Pref.GPanSensitivity, Consts.DefaultPanSensitivity);
            SetGFloat(Pref.GEdgePanSpeed, Consts.DefaultEdgePanSpeed);
            SetGBool(Pref.GSmoothDrag, Consts.DefaultSmoothDrag);
            SetGBool(Pref.GSmoothZoom, Consts.DefaultSmoothZoom);
            SetGBool(Pref.GGamepadIgnore, The.Platform.IgnoreGamepadDefault);
            SetGInt(Pref.GCancelDragDistancePixels, Consts.DefaultCancelDragDistancePx);
            SetGInt(Pref.GESCBehavior, Consts.DefaultEscBehavior);
            // VMouse settings
            SetGFloat(Pref.GVMouseSensitivity, Consts.DefaultVMouseSens);
            SetGFloat(Pref.GVMouseAccel, Consts.DefaultVMouseAccel);
            SetGFloat(Pref.GVMouseMaxSpeed, Consts.DefaultVMouseMaxSpeed);
            // Deadzone
            SetGFloat(Pref.GDeadZoneMin, Consts.DefaultDeadZoneMin);
            SetGFloat(Pref.GDeadZoneMax, Consts.DefaultDeadZoneMax);
            The.Bindings.SetDeadZone(Consts.DefaultDeadZoneMin, Consts.DefaultDeadZoneMax);
            The.SysSig.SetCursor.Send(UIPoolRoot.LastCursor);

            foreach (var action in UIActivator.FindUIInputs()) {
                action.RemoveAllBindingOverrides();
            }
            foreach (var binding in The.Bindings.List) {
                if (!binding.InputAction.enabled) {
                    binding.Enable();
                }
                binding.ResetToDefaults();
            }
            UIPoolRoot.Activator.SetClickAction(
                The.Bindings.GetBinding(ActionType.Click).InputAction);
            The.SaveBindingOverrides();
            ReloadTab();
            WPopup.Create(true, IconId.CInfo, T.InputReset, T.InputResetInfo)
                .Spawn(UIPoolRoot.SystemPopupContainer);
        }

        protected override void BeforeUnloadTab() {
            The.SysSig.BindingChanged.RemoveListener(OnBindingChanged);
            The.SaveBindingOverrides();
        }

        private bool wasUsingVMouse;

        public override void Update() {
            base.Update();
            // Handle device changes, etc.
            if (Gamepad.current != lastGamepad) {
                lastGamepad = Gamepad.current;
                // Perhaps reload or something, but for now skip
            }
            var isUsingVMouse = VirtualMouseInput.IsCurrent;
            if (wasUsingVMouse != isUsingVMouse) {
                wasUsingVMouse = isUsingVMouse;
                The.Bindings.ValidateConflicts();
            }
        }
    }
}
