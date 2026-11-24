using System;
using System.Collections.Generic;
using System.IO;
using Game.CodeGen;
using Game.Constants;
using Game.Mods;
using Game.UI;
using Game.Platform;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageSystem : BaseSettingsPage {
        // A getter: pages register before the language loads, so a list built then stays English
        private static List<string> UnitsOptions => new() {
            T.UnitsMetric,
            T.UnitsImperial,
        };

        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageSystem());
        }

        public const string PageId = "System";
        public override string Id => PageId;
        public override int Order => 5;
        public override string TabName => T.NounSystem;

        protected override void LoadTab() {
            int idx = 0;

            // Units
            AddDropdown(idx++, T.UnitsOfMeasurement, UnitsOptions,
                GInt(Pref.GUnits, Consts.DefaultUnits), (opt) => {
                SetGInt(Pref.GUnits, opt);
            });

            // Run in BG / Disable Achievements
            AddTogglePair(idx++, T.RunInBg,
                    GBool(Pref.GRunInBG, Consts.DefaultRunInBG), (v) => {
                SetGBool(Pref.GRunInBG, v);
                Application.runInBackground = v;
            }, null,


            T.DisableAchievements, GBool(Pref.GDisableAchievements, false), (v) => {
                SetGBool(Pref.GDisableAchievements, v);
            });

            WToggle2 lastSaveToggles = null;
            lastSaveToggles = AddTogglePair(idx++,
                T.GameLoadLastSave, GBool(Pref.GLoadLastSave, true), (v) => {
                    SetGBool(Pref.GLoadLastSave, v);
                    UpdateDemoModeToggle(v, lastSaveToggles);
                    if (!v) {
                        lastSaveToggles.Toggle2.SetValue(false);
                    }
                }, T.GameLoadLastSaveTip,

                T.DisableDemoMode, GBool(Pref.GDisableDemoMode, false), (v) => {
                    SetGBool(Pref.GDisableDemoMode, v);
                    // TODO stop demo mode flythrough and pause the game if it was unpaused / demo mode was active
                    // and restart the demo mode if it was disabled if v == true
                }, T.DisableDemoModeTip
            );

            UpdateDemoModeToggle(GBool(Pref.GLoadLastSave, true), lastSaveToggles);

            AddToggle(idx++,
            T.AutoSavePeriodic, GBool(Pref.GAutoSavePeriodic, true), (v) => {
                SetGBool(Pref.GAutoSavePeriodic, v);
            }, null);

            var resetBtn = AddFooterButton(0, T.FullReset, OnFullReset);
            resetBtn.OverrideButtonTheme("Red");
            ShowFooter(true);
        }

        private static void UpdateDemoModeToggle(bool v, WToggle2 lastSaveToggles) {
            lastSaveToggles.Toggle2.SetInteractable(v);
            if (v) {
                lastSaveToggles.Toggle2.SetText(T.DisableDemoMode);
            } else {
                lastSaveToggles.Toggle2.SetText(Texts.Grey(T.DisableDemoMode));
            }
        }

        private void OnFullReset() {
            WPopup.Create(true, IconId.CWarning, T.FullReset, null, T.FullResetAbout)
                .WithAction2(T.Cancel, null)
                .WithAction(T.Confirm, PlatformUtils.FullReset)
                .Spawn(UIPoolRoot.SystemPopupContainer);
        }

    }
}
