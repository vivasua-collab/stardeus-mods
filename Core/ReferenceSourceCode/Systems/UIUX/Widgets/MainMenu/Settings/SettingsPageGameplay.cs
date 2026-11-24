using System;
using System.Collections.Generic;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageGameplay : BaseSettingsPage {

        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageGameplay());
        }

        public const string PageId = "Gameplay";
        public override string Id => PageId;
        public override int Order => 10;
        public override string TabName => T.MenuSettingsGameplay;

        protected override void LoadTab() {
            ShowFooter(false);
            int idx = 0;

            AddHeader(idx++, T.MenuSettingsGameplay);

            // Auto expand tooltips
            // AddToggle(idx++, T.AutoExpandTips,
            //         GBool(Pref.GAutoExpandTooltips, Consts.AutoExpandTipsDefault), (v) => {
            //     SetGBool(Pref.GAutoExpandTooltips, v);
            // });
            // Create energy connectors upon releasing a link

            AddTogglePair(idx++,
                T.ShowHints, GBool(Pref.GShowHints, true), (v) => {
                    SetGBool(Pref.GShowHints, v);
                }, null,
                T.GradualUiReveal, GBool(Pref.GGradualUIReveal, true), (v) => {
                    SetGBool(Pref.GGradualUIReveal, v);
                });

            AddToggle(idx++, T.DragReleaseBuildCon,
                    GBool(Pref.GDragRelBuildCon, Consts.DefaultDragRelBuildCon), (v) => {
                SetGBool(Pref.GDragRelBuildCon, v);
            });

            AddHeader(idx++, T.NounVisuals);

            AddTogglePair(idx++,
                T.ShowBuildGuidelines, GBool(Pref.GShowBuildGuidelines, true), (v) => {
                    SetGBool(Pref.GShowBuildGuidelines, v);
                }, null,
                T.GridShowBounds, GBool(Pref.GShowGridBounds, true), (v) => {
                    SetGBool(Pref.GShowGridBounds, v);
                });


            AddTogglePair(idx++,
                T.FocusOnItemsAfterTrade, GBool(Pref.GFocusOnItemsAfterTrade, true), (v) => {
                    SetGBool(Pref.GFocusOnItemsAfterTrade, v);
                }, null,
                T.FocusCameraOnEvents, GBool(Pref.GFocusOnEvents, true), (v) => {
                    SetGBool(Pref.GFocusOnEvents, v);
                });

            AddTogglePair(idx++, T.ShowOnlySelectedNames, GBool(Pref.GShowOnlySelectedNames, false), (v) => {
                SetGBool(Pref.GShowOnlySelectedNames, v);
                foreach (var b in A.S.Beings.All) {
                    b.Name.ShowOnSelectChanged(v);
                }
            }, null,
                T.ShowElectricityMiniChart, GBool(Pref.GShowElectricityMiniChart, true), (v) => {
                    SetGBool(Pref.GShowElectricityMiniChart, v);
                }
            );

            var tooltipDelay = GFloat(Pref.GTooltipDelay, Consts.DefaultTooltipDelay);
            var tooltipDelaySlider = AddSlider(idx++, T.SettingTooltipDelay,
                Mathf.InverseLerp(Consts.TooltipDelayMin, Consts.TooltipDelayMax, tooltipDelay), (v) => {
                    SetGFloat(Pref.GTooltipDelay, SnapTooltipDelay(v));
                }, T.SettingTooltipDelayTip);
            tooltipDelaySlider.Slider.SetValueTextFunc(
                (v) => T.SecondsAbbr_V1.F(SnapTooltipDelay(v).ToString("0.00")));
        }

        private static float SnapTooltipDelay(float sliderValue) {
            return Maths.ClampSnap(
                Mathf.Lerp(Consts.TooltipDelayMin, Consts.TooltipDelayMax, sliderValue),
                Consts.TooltipDelayMin, Consts.TooltipDelayMax, Consts.TooltipDelayStep);
        }
    }
}
