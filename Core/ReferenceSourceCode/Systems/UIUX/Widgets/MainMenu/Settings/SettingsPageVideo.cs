using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Modifiers;
using Game.Mods;
using Game.Platform;
using Game.Systems.Light;
using Game.UI;
using Game.Utils;
using Game.Visuals;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageVideo : BaseSettingsPage {
        // Getters: pages register before the language loads, so a list built then stays English
        private static List<string> PerfPresetOptions => new() {
            T.Auto, T.AdjCustom
        };
        private static List<string> TexSizeOptions => new() {
            T.TexFull, T.TexHalf, T.TexQuarter, T.PixelArt, T.PixelArtLowBudget
        };
        private static List<string> LightMapScaleOptions => new() {
            T.Auto, "1x", "1/2x", "1/3x", "1/4x", "1/5x", "1/6x"
        };
        private static (int value, string label)[] LightMapCellSizes => new[] {
            (0, T.Auto), (4, "4"), (8, "8"), (16, "16"), (32, "32"), (64, "64")
        };
        private static (int value, string label)[] LightMapMaxLights => new[] {
            (0, T.Auto), (4, "4"), (8, "8"), (12, "12"), (16, "16"), (24, "24"),
            (32, "32"), (48, "48"), (64, "64"), (96, "96"), (128, "128"),
            (256, "256"), (512, "512"), (1024, "1024")
        };

        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageVideo());
        }
        public const string PageId = "Video";
        public override string Id => PageId;
        public override int Order => 20;
        public override string TabName => T.MenuSettingsVideo;
        private string selectedRes;
        private int selectedMode;
        private int selectedVsync;
        private WDropdownWithLabel resolutionDropdown;
        private WDropdownWithLabel vsyncDropdown;
        private float newScale = -1f;
        private float newFontScale = -1f;
        private float newPadding = -1f;
        private bool hasUnappliedSettings;
        private WDropdownWithLabel ambientColorDropdown;
        protected override void LoadTab() {
            ShowFooter(hasUnappliedSettings);
            AddFooterButton(0, T.Apply, OnApply);

            int idx = 0;

            idx = AddMainSection(idx);
            idx = AddUISection(idx);
            idx = AddExperienceSection(idx);
            idx = AddPerformanceSection(idx);

            idx = AddDebugSection(idx);
        }

        private int AddDebugSection(int idx) {
            AddHeader(idx++, T.NounTroubleshooting);

            AddButton(idx++, T.RebuildGraphicsCache, RebuildGraphicsCache,
                T.RebuildGraphicsCacheTip_V1.F(ModLoader.GraphicsCacheDir));
            return idx;
        }

        private int AddPerformanceSection(int idx) {
            AddHeader(idx++, T.NounPerformance);

            var perfPreset = CachedPlayerPrefs.GetInt(Video.PerfPresetKey, 0);
            AddDropdown(idx++, T.NounPreset, PerfPresetOptions, perfPreset, (i) => {
                CachedPlayerPrefs.SetInt(Video.PerfPresetKey, i);
                if (i == 0 && perfPreset == 1) {
                    ResetPerfToDefaults();
                }
                ReloadTab();
            });

            // custom performance - show all the opts
            if (perfPreset != 0) {
                idx = AddCustomPerfOptions(idx);
            }

            return idx;
        }

        private int AddUISection(int idx) {
            // UI SECTION
            AddHeader(idx++, T.NounUserInterface);

            var themeIds = UITheme.OptionIds;
            var curTheme = Mathf.Max(0, themeIds.IndexOf(The.UITheme.Id));
            AddDropdown(idx++, T.NounTheme, UITheme.Options, curTheme, (idx) => {
                UITheme.SwitchTo(themeIds[idx]);
            });

            // Auto scale UI
            var useAutoScale = GBool(Pref.GUIAutoScale, true);
            AddToggle(idx++, T.UiAutoScale, useAutoScale, (v) => {
                SetGBool(Pref.GUIAutoScale, v);
                if (!v) {
                    var data = UIScale.GetScaleData();
                    var rs = data.def;
                    The.Prefs.SetFloat(Pref.GUIScale, rs);
                }
                ReloadTab();
            });

            if (!useAutoScale) {
                // UI Scale slider
                var data = UIScale.GetScaleData();
                var refScale = data.def;
                var scale = The.Prefs.GetFloatNonZero(Pref.GUIScale, PrefH.GUIScale, Consts.DefaultUIScale);
                newScale = scale;
                WSliderWithLabel slider = null;
                slider = AddSliderWithRange(idx++, T.UiScale, scale, data.min - 0.01f, data.max + 0.01f, (v) => {
                    newScale = Maths.ClampSnap(v, data.min - 0.01f, data.max + 0.01f, data.step);
                }, T.VideoUiScaleTip);
                slider.Slider.SetValueTextFunc((v) => $"{newScale:0.##}x");

            }

            // Font scale slider
            var fsm = The.Prefs.GetFloatNonZero(Pref.GUIFontScale, PrefH.GUIFontScale, Consts.UIFontScaleDefault);
            newFontScale = fsm;
            WSliderWithLabel slider2 = null;
            slider2 = AddSliderWithRange(idx++, T.NounFontScale, fsm,
                Consts.UIFontScaleMin, Consts.UIFontScaleMax, (v) => {
                    newFontScale = Maths.ClampSnap(v, Consts.UIFontScaleMin, Consts.UIFontScaleMax, 0.05f);
                }, T.VideoUiFontScaleTip);
            slider2.Slider.SetValueTextFunc((v) => $"{newFontScale:0.##}x");

            idx = AddWideScreenPaddingControls(idx);
            return idx;
        }

        private int AddWideScreenPaddingControls(int idx) {
            bool isWide = (float) Screen.width / Screen.height > 16f / 9f;
            #if UNITY_EDITOR
            // isWide = true;
            #endif
            if (!isWide) {
                return idx;
            }
            var refPadding = MultiMonitorPadding.SuggestPaddingAmount();
            var maxPadding = MultiMonitorPadding.MaxPaddingAmount();
            var useAutoPadding = GBool(Pref.GUIAutoPadding, Consts.DefaultWidescreenAutoPadding);

            AddToggle(idx++, T.UiAutoPadding, useAutoPadding, (v) => {
                SetGBool(Pref.GUIAutoPadding, v);
                ReloadTab();
            });

            if (!useAutoPadding) {
                // Padding slider
                newPadding = GFloat(Pref.GUIPadding, refPadding);
                WSliderWithLabel slider = null;
                slider = AddSliderWithRange(idx++, T.UiPadding, newPadding, 0f, maxPadding, (v) => {
                    newPadding = v;
                    var padAmt = Screen.width * newPadding;
                    if (Screen.width - 2 * padAmt < 1200) {
                        newPadding = ((Screen.width - 1200) / 2f) / Screen.width;
                        slider.Slider.SetValue(newPadding, notify: false);
                    }
                    ShowApplyButton();
                }, T.UiPaddingTip);
                slider.Slider.SetValueTextFunc((v) => {
                    return Units.Percentage(v);
                });
            }

            return idx;
        }

        private void ShowApplyButton() {
            hasUnappliedSettings = true;
            ShowFooter(true);
        }

        private int AddExperienceSection(int idx) {
            AddHeader(idx++, T.NounExperience);

            // Post processing profile
            var ppIds = PostProcessing.Ids;
            var ppNames = ppIds.Select(PostProcessing.DisplayName).ToList();
            var ppSel = Mathf.Max(0, ppIds.IndexOf(PostProcessing.CurrentId));
            AddDropdown(idx++, "post.processing".T(), ppNames, ppSel,
                (opt) => PostProcessing.SwitchTo(ppIds[opt]), "post.processing.tip".T());

            // Ambient color
            var ambColor = GStr(Pref.GAmbColor, Consts.AmbientLightColorDefault);
            var ambColorOptions = AmbientLightDef.All.Values.OrderBy(c => c.SortOrder).Select(c => c.NameT).ToList();
            int selAmbColor = ambColorOptions.IndexOf(
                AmbientLightDef.All.Values.FirstOrDefault(c => c.Id == ambColor)?.NameT ?? AmbientLightDef.Default.NameT);
            ambientColorDropdown = AddDropdown(idx++, T.AmbColor, ambColorOptions, selAmbColor,
                (opt) => OnAmbColor(ambColorOptions, opt));
            UpdateAmbColorDropdown();

            // Room ambient light slider
            var curVal = GFloat(Pref.GRoomDimMul, Consts.DefaultRoomDimMul);
            AddSlider(idx++, T.RoomAmbientLightAmt, curVal, (v) => {
                SetGFloat(Pref.GRoomDimMul, v);
                MapRenderer.SetRoomDimMul();
            }, T.RoomAmbientLightAmtTip);

            // Light intensity slider
            var lightIntensity = GFloat(Pref.GLightIntensity, Consts.DefaultLightIntensity);
            AddSliderWithRange(idx++, T.LightIntensity, lightIntensity,
                Consts.MinLightIntensity, Consts.MaxLightIntensity, (v) => {
                SetGFloat(Pref.GLightIntensity, v);
            }, T.LightIntensityTip);

            // Screen Shake | Pause Frame
            AddTogglePair(idx++,
                T.ScreenShake, Pref.GScreenShake, true, null,
                T.EnablePauseFrame, Pref.GPauseFrame, true, null);

            // Cursor Light | Immersive Icons
            AddTogglePair(idx++,
                T.CursorLight, GBool(Pref.GCursorLight, true),
                    (v) => SetGBool(Pref.GCursorLight, v), T.CursorLightTip,
                T.ImmersiveIcons, GBool(Pref.GImmersiveIcons, false),
                    (v) => {
                        SetGBool(Pref.GImmersiveIcons, v);
                        if (The.R?.State != null) {
                            foreach (var b in The.R.State.Beings.All) {
                                b.Name.ResetName();
                            }
                        }
                    }, T.ImmersiveIconsTip);

            // Animate space background | Heat distortion
            AddTogglePair(idx++,
                T.AnimatedBg, Pref.GAnimatedBackground, true, null,
                T.VisibleHeatDistortion, Pref.GHeatDistortion, true, T.VisibleHeatDistortionTip);

            // Floor shading (floor contact shadows and hull rim light) | Ambient motion
            AddTogglePair(idx++,
                T.FloorShading, Pref.GFloorShading, TileShading.DefaultOn, T.FloorShadingTip,
                T.AmbientMotion, Pref.GAmbientMotion, AmbientMotion.DefaultOn, T.AmbientMotionTip);

            // Glow (bloom on effects brighter than white) | Light flashes (explosions, shots)
            AddTogglePair(idx++,
                T.Glow, Pref.GGlow, Glow.DefaultOn, T.GlowTip,
                T.LightFlashes, Pref.GLightFlashes, LightFlashes.DefaultOn, T.LightFlashesTip);


            // Disable Device animations | Show Cosmetic Particles
            AddTogglePair(idx++,
                T.DisableDeviceAnimations, GBool(Pref.GNoDeviceAnim, false), (v) => {
                    SetGBool(Pref.GNoDeviceAnim, v);
                    if (GameState.IsCurrent(The.R?.State)) {
                        PlatformUtils.SuggestRestart();
                    }
                    }, T.DisableDeviceAnimationsTip,
                T.CosmeticParticles, GBool(Pref.GCosmeticParticles, true), (v) => {
                    SetGBool(Pref.GCosmeticParticles, v);
                    PlatformUtils.SuggestRestart();
                }, T.CosmeticParticlesTip);

            // Disable All Visual Effects | Display Space Nebula
            AddTogglePair(idx++,
                T.DisableAllParticles, GBool(Pref.GDisableVFX, false), (v) => {
                    SetGBool(Pref.GDisableVFX, v);
                    PlatformUtils.SuggestRestart();
                }, T.DisableAllParticlesTip,
                T.DisplayNebula, GBool(Pref.GNebulaOn, true),
                    (v) => SetGBool(Pref.GNebulaOn, v), T.DisplayNebulaTip);
            return idx;
        }

        private int AddMainSection(int idx) {
            // VIDEO SETTINGS

            // Display (only if multiple displays)
            var displays = Video.DisplayOptions(out int currentDisplay, out _);
            if (displays.Count > 1) {
                AddDropdown(idx++, T.VideoDisplay, displays, currentDisplay, OnDisplayChange);
            }

            // Resolution
            var resolutions = Video.ResolutionOptions(out int currentRes, 1200, 720);
            selectedRes = resolutions[currentRes];
            resolutionDropdown = AddDropdown(idx++, T.VideoResolution, resolutions, currentRes,
                (idx) => OnSetResolution(resolutions[idx]));

            // VSYNC - index 0 = Auto, 1 = Off, 2..5 = specific interval
            var vsyncChoice = Video.VSyncChoice;
            selectedVsync = vsyncChoice == Video.VSyncAuto ? 0 : vsyncChoice + 1;
            vsyncDropdown = AddDropdown(idx++, T.VideoVsync, Video.VSyncOptions, selectedVsync,
                OnSetVsync, T.VideoVsyncTip);

            // Screen mode
            selectedMode = Video.CurrentScreenMode;
            AddDropdown(idx++, T.VideoMode, Video.ScreenModeOptions, selectedMode, OnSetVideoMode);

            return idx;
        }

        private void ResetPerfToDefaults() {
            // #if UNITY_EDITOR
            // // for quicker rebuilds
            // OnTexSizeChange(1);
            // #else
            OnTexSizeChange(Video.GetRecommendedTextureSize(0));
            // #endif
            OnLightMapScaleChange(0);
            OnLightMapCellSizeChange(0);
            OnLightMapMaxLightsChange(0);
        }

        private int AddCustomPerfOptions(int idx) {
            // Texture size
            AddDropdown(idx++, T.TextureSize, TexSizeOptions,
                CachedPlayerPrefs.GetInt(Res.TextureSizePref, 0),
                OnTexSizeChange, T.VideoTextureScaleTip);

            // Lightmap Scale
            var lmScale = GInt(Pref.GLightMapScale, 0);
            if (lmScale >= LightMapScaleOptions.Count || lmScale < 0) {
                lmScale = 0;
            }
            AddDropdown(idx++, T.LightmapScale, LightMapScaleOptions, lmScale, OnLightMapScaleChange,
                T.LightmapScaleTip);

            // Lightmap Cell Size
            var lmCellSize = GInt(Pref.GLightMapCellSize, 0);
            var lightmapCellSizeIdx = Array.FindIndex(LightMapCellSizes, t => t.value == lmCellSize);
            if (lightmapCellSizeIdx < 0) {
                lightmapCellSizeIdx = 0;
            }
            var cellSizeLabels = LightMapCellSizes.Select(t => t.label).ToList();
            AddDropdown(idx++, T.LightmapCellSize, cellSizeLabels,
                lightmapCellSizeIdx, (opt) => OnLightMapCellSizeChange(LightMapCellSizes[opt].value),
                T.LightmapCellSizeTip);

            // Lightmap Max Lights
            var mLights = GInt(Pref.GLightMapMaxLightsPerCell, 0);
            var maxLightsIdx = Array.FindIndex(LightMapMaxLights, t => t.value == mLights);
            if (maxLightsIdx < 0) {
                maxLightsIdx = 0;
            }
            var maxLightsLabels = LightMapMaxLights.Select(t => t.label).ToList();
            AddDropdown(idx++, T.LightmapMaxLightsPerCell, maxLightsLabels,
                maxLightsIdx, (opt) => OnLightMapMaxLightsChange(LightMapMaxLights[opt].value),
                T.LightmapMaxLightsPerCellTip);

            return idx;
        }

        public static void RebuildGraphicsCache() {
            var folder = ModLoader.GraphicsCacheDir;
            var hadError = false;
            string error = null;
            try {
                Files.ClearDirectory(folder, true);
            } catch (Exception ex) {
                hadError = true;
                error = ex.Message;
                D.LogEx(ex, "Failed deleting cache folder: {0}", folder);
            }
            var deleted = !Directory.Exists(folder);
            if (!deleted) {
                var sb = Caches.StringBuilder;
                sb.Clear();
                sb.AppendFormat("Failed deleting graphics cache folder:\n<b>{0}</b>", folder);
                if (hadError) {
                    sb.AppendFormat("Error: {0}\n", error);
                }
                sb.AppendFormat("\nPlease try deleting this folder manually after exiting the game");
                WPopup.Create(true, IconId.CWarning, T.OperationFailed, null, sb.ToString())
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            }
            if (deleted) {
                PlatformUtils.SuggestRestart();
            }
        }

        private void UpdateAmbColorDropdown() {
            if (ambientColorDropdown == null) { return; }
            if (The.R?.State != null && ModifierFogOfWar.IsEnabled) {
                ambientColorDropdown.Dropdown.SetInteractable(false);
                AddTip(ambientColorDropdown, $"{T.StateDisabled}: {T.AmbColorDark}");
            } else {
                ambientColorDropdown.Dropdown.SetInteractable(true);
                AddTip(ambientColorDropdown, T.VideoAmbientColorTip);
            }
        }

        private void OnAmbColor(List<string> options, int opt) {
            var ambC = options[opt];
            foreach (var col in AmbientLightDef.All.Values) {
                if (col.NameT == ambC) {
                    The.Prefs.SetString(Pref.GAmbColor, col.Id);
                    The.SysSig.AmbientColorChanged.Send(col);
                    break;
                }
            }
        }

        private void OnDisplayChange(int option) {
            Coroutines.StartDirect(Video.MoveToDisplay(option, () => {
                Coroutines.OnNextFrame(() => {
                    // Refresh resolution options after display change
                    var resolutions = Video.ResolutionOptions(
                        out int currentRes, 1200, 720);
                    selectedRes = resolutions[currentRes];
                    // a dropdown index, 0 is Auto (not Video.VSyncAuto, the stored choice)
                    selectedVsync = 0;
                    resolutionDropdown.Dropdown.SetOptions(resolutions, currentRes);
                    vsyncDropdown.Dropdown.SetOptions(Video.VSyncOptions, 0);
                    Video.VSyncChoice = Video.VSyncAuto;
                    Video.ApplyVsync(false);
                });
            }));
        }

        private void OnLightMapScaleChange(int opt) {
            var scales = new int[] { 0, 1, 2, 3, 4, 5, 6 };
            The.Prefs.SetInt(Pref.GLightMapScale, scales[opt]);
            if (The.R?.State != null) {
                The.SysSig.ResolutionChanged.Send();
            }
        }

        private void OnLightMapCellSizeChange(int size) {
            The.Prefs.SetInt(Pref.GLightMapCellSize, size);
            if (The.R?.State != null) {
                The.SysSig.ResolutionChanged.Send();
            }
        }

        private void OnLightMapMaxLightsChange(int size) {
            The.Prefs.SetInt(Pref.GLightMapMaxLightsPerCell, size);
            if (The.R?.State != null) {
                The.SysSig.ResolutionChanged.Send();
            }
        }

        private void OnTexSizeChange(int opt) {
            var maxSize = ModLoader.MaxTextureSize;
            if (opt < maxSize) {
                WPopup.Create(true, IconId.CWarning, T.Warning, T.MaxTextureSizeTooHigh)
                    .Spawn(UIPoolRoot.SystemPopupContainer);
            }
            var curSize = CachedPlayerPrefs.GetInt(Res.TextureSizePref, -1);
            if (opt != curSize) {
                CachedPlayerPrefs.SetInt(Res.TextureSizePref, opt);
                PlatformUtils.SuggestRestart();
            }
        }

        public override void Update() {
            // Don't update during mouse drag
            if (UnityEngine.InputSystem.Mouse.current != null &&
                    (UnityEngine.InputSystem.Mouse.current.leftButton.isPressed ||
                        UnityEngine.InputSystem.Mouse.current.rightButton.isPressed)) {
                return;
            }

            UpdateFloatPrefIfChanged(Pref.GUIFontScale, PrefH.GUIFontScale,
                Consts.UIFontScaleDefault, newFontScale, () => {
                The.UITheme.Reload();
            });

            UpdateFloatPrefIfChanged(Pref.GUIScale, PrefH.GUIScale,
                Consts.DefaultUIScale, newScale, () => The.SysSig.ResolutionChanged.Send());

            UpdateFloatPrefIfChanged(Pref.GUIPadding, PrefH.GUIPadding,
                0f, newPadding, null);
        }

        private void UpdateFloatPrefIfChanged(string pref, int prefHash, float defaultVal, float newVal, Action onChanged) {
            if (newVal == -1f) { return; }
            var currentVal = The.Prefs.GetFloatNonZero(pref, prefHash, defaultVal);
            if (newVal != currentVal) {
                D.Warn("Setting {0}: {1} -> {2}", pref, currentVal, newVal);
                The.Prefs.SetFloat(pref, newVal);
                onChanged?.Invoke();
            }
        }

        private void OnSetVsync(int opt) {
            selectedVsync = opt;
            ShowApplyButton();
        }

        private void OnSetVideoMode(int i) {
            selectedMode = i;
            ShowApplyButton();
        }

        protected override void BeforeUnloadTab() {
            ShowFooter(false);
            ambientColorDropdown = null;
        }

        private void OnApply() {
            Video.SetResolution(selectedRes, selectedMode);

            // Convert dropdown index back to vsync choice: 0 = Auto (-1), 1+ = explicit count
            var vsyncChoice = selectedVsync == 0 ? Video.VSyncAuto : selectedVsync - 1;
            Video.VSyncChoice = vsyncChoice;
            if (vsyncChoice == 0) {
                CachedPlayerPrefs.SetInt(Video.VsyncOffConfirmKey, 1);
            } else {
                PlayerPrefs.DeleteKey(Video.VsyncOffConfirmKey);
            }

            Video.ApplyVsync(false);
            The.SaveGlobalSettings();

            hasUnappliedSettings = false;
            ShowFooter(false);
        }

        private void OnSetResolution(string res) {
            selectedRes = res;
            ShowApplyButton();
        }
    }
}
