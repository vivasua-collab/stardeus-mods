using System;
using System.Collections.Generic;
using System.Linq;
using Game.Audio;
using Game.CodeGen;
using Game.Constants;
using Game.Input;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class SettingsPageAudio : BaseSettingsPage {
        [SelfInit]
        private static void Register() {
            RegisterPage(new SettingsPageAudio());
        }

        public const string PageId = "Audio";
        public override string Id => PageId;
        public override int Order => 30;
        public override string TabName => T.MenuSettingsAudio;

        private WSliderWithLabel maxSilenceSlider;
        private readonly Dictionary<string, WSliderWithLabel> volumeSliders = new();
        private readonly Dictionary<string, WToggleText> togglers = new();

        private bool showSoundtrack;

        protected override void LoadTab() {
            AddFooterButton(0, T.ResetToDefaults, LoadDefaults);
            ShowFooter(true);

            int idx = 0;
            idx = AddMainSection(idx);
            idx = AddExperienceSection(idx);
            idx = AddSoundtrackSection(idx);
        }

        private int AddSoundtrackSection(int idx) {
            AddHeader(idx++, T.Soundtrack);

            if (!showSoundtrack) {
                // Soundtrack button
                AddButton(idx++, T.VerbCustomize, () => {
                    showSoundtrack = true;
                    ReloadTab();
                });
                return idx;
            }

            // Load soundtrack toggles
            foreach (var t in Track.All) {
                AddToggleWithButton(idx++, t.Name, Soundtrack.IsEnabled(t), (v) => {
                    Soundtrack.SetEnabled(t, v);
                    SoundMixer.OST.ClearQueue();
                },
                string.Join(", ", t.Moods.Select(m => Strings.CapitalizeFirst(m))),
                GlyphMap.TriangleRight, () => {
                    SoundMixer.PlayNow(t);
                }, $"{T.PlayTrack}: {t.Name}");
            }
            return idx;
        }

        protected override void BeforeUnloadTab() {
            volumeSliders.Clear();
            togglers.Clear();
        }

        private int AddMainSection(int idx) {
            // Volume sliders
            foreach (var vol in SoundMixer.VolumeKeys) {
                volumeSliders[vol] = AddSlider(idx++, vol.T(), SoundMixer.GetVolume(vol), (v) => {
                    SoundMixer.SetVolume(vol, v);
                });
            }
            return idx;
        }
        private int AddExperienceSection(int idx) {

            AddHeader(idx++, T.NounExperience);

            // Max silence slider
            var maxSilenceTime = GFloat(Pref.GAudioMaxSilenceSeconds, Consts.DefaultAudioMaxSilence);
            maxSilenceSlider = AddSlider(idx++, T.AudioSilenceBetweenTracks, Mathf.InverseLerp(0f, 600f, maxSilenceTime), (v) => {
                var newVal = Mathf.Lerp(0f, 600f, v);
                SetGFloat(Pref.GAudioMaxSilenceSeconds, newVal);
            }, T.AudioMaxSilenceTip);
            maxSilenceSlider.Slider.SetValueTextFunc((v) => SilencePeriodLabel(Mathf.Lerp(0f, 600f, v)));

            // UI Hover Sounds and Cutoff Music toggle pair
            var pair1 = AddTogglePair(idx++,
                T.AudioUiHover, GBool(Pref.GAudioUIHoverSounds, true), (v) => {
                    SetGBool(Pref.GAudioUIHoverSounds, v);
                }, null,
                T.CutoffMusic, GBool(Pref.GCutoffMusic, true), (v) => {
                    SetGBool(Pref.GCutoffMusic, v);
                }, T.AudioMusicCutoffTip);
            togglers[Pref.GAudioUIHoverSounds] = pair1.Toggle1;
            togglers[Pref.GCutoffMusic] = pair1.Toggle2;

            // Cutoff Ambience and FX toggle pair
            var pair2 = AddTogglePair(idx++,
                T.CutoffAmbience, GBool(Pref.GCutoffAmbience, true), (v) => {
                    SetGBool(Pref.GCutoffAmbience, v);
                }, T.AudioAmbCutoffTip,
                T.CutoffFx, GBool(Pref.GCutoffFX, true), (v) => {
                    SetGBool(Pref.GCutoffFX, v);
                }, T.AudioFxCutoffTip);
            togglers[Pref.GCutoffAmbience] = pair2.Toggle1;
            togglers[Pref.GCutoffFX] = pair2.Toggle2;

            togglers[Pref.GVoiceAlerts] = AddToggle(idx++,
                T.AudioVoiceAlerts, Pref.GVoiceAlerts, true, T.AudioVoiceAlertsTip);

            return idx;
        }

        private string SilencePeriodLabel(float silence) {
            if (silence < 1f) {
                return T.SilencePeriodsNone;
            } else {
                var from = Units.SecondsToShortTime(silence *
                    Consts.AudioSilenceMinMultiplier);
                var to = Units.SecondsToShortTime(silence);
                return $"{from} - {to}";
            }
        }

        private void LoadDefaults() {
            maxSilenceSlider.Slider.SetValue(Mathf.InverseLerp(0f, 600f,
                Consts.DefaultAudioMaxSilence));
            foreach (var v in volumeSliders) {
                v.Value.Slider.SetValue(Consts.VolumeDefaults[v.Key]);
            }
            foreach (var t in togglers) {
                t.Value.SetValue(Consts.AudioSettingDefaults[t.Key]);
            }
        }

        public override void Update() {
            base.Update();
            // Update volume sliders to reflect current values
            foreach (var s in volumeSliders) {
                s.Value.Slider.SetValue(SoundMixer.GetVolume(s.Key));
            }
        }
    }
}
