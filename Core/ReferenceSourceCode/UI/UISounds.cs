#if !PRODUCTION_BUILD
// #define TRACE_SOUNDS
// #define DEBUG_CLOCK_SOUNDS
#endif
using System;
using Game.Audio;
using Game.CodeGen;
using Game.Constants;
using Game.Utils;
using KL.Clock;
using KL.Randomness;
using KL.Utils;
using UnityEngine;

namespace Game.UI {
    public static class UISounds {
        public enum HoverType {
            Default = 0,
            Lowest = 1,
            Low = 2,
            Mid = 3,
            High = 4,
            Silent = 5,
        }
        public enum ClickType {
            Default = 0,
            Small = 1,
            Big = 2,
            High = 3,
            Silent = 4,
        }
        [SelfInit]
        private static void Cleanup() {
            lastHoverTime = 0;
            isUIhoverSounds = true;
            lastDigitalConfirmFrame = 0;
            lastDigitalShortFrame = 0;
            lastDigitalCancelFrame = 0;
            lastEventWarningFrame = 0;
            lastActionDeniedTime = 0;
            lastEmergencyFrame = 0;
            lastPopupFrame = 0;
            lastEventSoundTime = 0;
            lastFanfareTime = 0;
            #if TRACE_SOUNDS
            lastDigitalCancelTrace = null;
            lastDigitalConfirmTrace = null;
            lastDigitalShortTrace = null;
            lastEmergencyTrace = null;
            lastActionDeniedTrace = null;
            lastEventWarningTrace = null;
            #endif
        }
        public static bool isUIhoverSounds = true;
        private const float hoverVol = 0.15f;
        private const float clickVol = 0.25f;
        private const float eventVol = 0.5f;
        private const float deniedVol = 0.25f;
        private const float dialogueVol = 0.5f;
        private const float fanfareVol = 0.4f;
        // How long after an event sound the fanfare beep stays silent
        private const float fanfareSilenceWindow = 0.5f;
        // Minimum time between two fanfare beeps
        private const float fanfareMinInterval = 0.25f;
        private static double lastHoverTime;
        private static double lastEventSoundTime;
        private static double lastFanfareTime;
        #if TRACE_SOUNDS
        private static string lastDigitalConfirmTrace;
        private static string lastDigitalShortTrace;
        private static string lastDigitalCancelTrace;
        private static string lastEmergencyTrace;
        private static string lastActionDeniedTrace;
        private static string lastEventWarningTrace;
        #endif
        public static void PlayButtonClick() {
            PlayDigitalConfirm();
        }
        public static void PlayCancel() {
            PlayDigitalCancel();
        }

        /// <summary>
        /// To prevent hover sound after rebuilding search text input when player types
        /// </summary>
        public static void SkipNextButtonHover() {
            lastHoverTime = GameTimer.realtimeSinceStartupAsDouble;
        }

        public static void PlayButtonHover(HoverType type) {
            if (!isUIhoverSounds) { return; }
            var time = GameTimer.realtimeSinceStartupAsDouble;
            if (time - lastHoverTime < 0.05f) { return; }
            lastHoverTime = time;
            PlayHover(type);
        }

        public static void PlayLongPressStart() {
            SoundMixer.AudioUI.Stop();
            digitalLongsLast = Rng.UFromExcept(digitalLongs, digitalLongsLast);
            var clip = The.Sounds.Find(digitalLongsLast);
            SoundMixer.AudioUI.volume = hoverVol * 0.5f;
            SoundMixer.AudioUI.clip = clip;
            SoundMixer.AudioUI.Play();
        }

        public static void PlayLongPressFail() {
            SoundMixer.AudioUI.Stop();
            digitalLongCancelsLast = Rng.UFromExcept(digitalLongCancels, digitalLongCancelsLast);
            var clip = The.Sounds.Find(digitalLongCancelsLast);
            SoundMixer.AudioUI.volume = hoverVol;
            SoundMixer.AudioUI.clip = clip;
            SoundMixer.AudioUI.Play();
        }

        public static void PlayLongPressDone() {
            SoundMixer.AudioUI.Stop();
            PlayDigitalConfirm();
        }
        public static void PlayAchievement() {
            SendEventSound("FX/Achievement", eventVol);
            The.SysSig.GamepadRumble.Send(0.25f, 0.2f, 0.3f);
        }

        private static int lastPopupFrame;

        public static void PlayPopup() {
            var f = GameTimer.frameCount;
            if (lastPopupFrame == f) {
                return;
            }
            lastPopupFrame = f;
            digitalPopupsLast = Rng.UFromExcept(digitalPopups, digitalPopupsLast);
            The.SysSig?.PlayUISound.Send(digitalPopupsLast, eventVol);
        }

        private static int lastDigitalShortFrame;
        public static void PlayHover(HoverType type) {
            if (type == HoverType.Silent) { return; }
            var f = GameTimer.frameCount;
            if (lastDigitalShortFrame == f) {
                #if TRACE_SOUNDS
                D.Err("Playing digital short twice this frame! {0}", lastDigitalShortTrace);
                #else
                // D.Err("Playing digital short twice this frame! Trace off");
                #endif
                return;
            }
            lastDigitalShortFrame = f;
            #if TRACE_SOUNDS
            lastDigitalShortTrace = Environment.StackTrace;
            #endif

            int idx;
            switch (type) {
                default:
                case HoverType.High: idx = 3; break;
                case HoverType.Mid: idx = 2; break;
                case HoverType.Low: idx = 1; break;
                case HoverType.Lowest: idx = 0; break;
            }

            var sound = uiHoverSoundLast[idx];
            sound = Rng.UFromExcept(uiHoverSounds[idx], sound);
            uiHoverSoundLast[idx] = sound;
            The.SysSig.PlayUISound.Send(sound, hoverVol);
        }

        public static void PlayClick(ClickType type, bool down) {
            int idx;
            switch (type) {
                case ClickType.Small: idx = 0; break;
                case ClickType.Big: idx = 1; break;
                default:
                case ClickType.High: idx = 2; break;
                case ClickType.Silent: return;
            }

            var last = down ? uiClickDownSoundLast : uiClickUpSoundLast;
            var sounds = down ? uiClickDownSounds : uiClickUpSounds;
            var sound = last[idx];
            sound = Rng.UFromExcept(sounds[idx], sound);
            last[idx] = sound;
            The.SysSig.PlayUISound.Send(sound, hoverVol);
        }

        private static int lastDigitalConfirmFrame;
        public static void PlayDigitalConfirm() {
            // D.Warn("Playing digital confirm");
            var f = GameTimer.frameCount;
            if (lastDigitalConfirmFrame == f) {
                #if TRACE_SOUNDS
                D.Err("Playing digital confirm twice this frame! {0}", lastDigitalConfirmTrace);
                #else
                // D.Err("Playing digital confirm twice this frame! Trace off");
                #endif
                return;
            }
            lastDigitalConfirmFrame = f;
            #if TRACE_SOUNDS
            lastDigitalConfirmTrace = Environment.StackTrace;
            #endif
            digitalConfirmsLast = Rng.UFromExcept(digitalConfirms, digitalConfirmsLast);
            The.SysSig.PlayUISound.Send(digitalConfirmsLast, clickVol);
        }
        private static int lastDigitalCancelFrame;
        public static void PlayDigitalCancel() {
            #if UNITY_EDITOR
            D.Warn("Playing digital cancel");
            #endif
            var f = GameTimer.frameCount;
            if (lastDigitalCancelFrame == f) {
                #if TRACE_SOUNDS
                D.Err("Playing digital cancel twice this frame! {0}", lastDigitalCancelTrace);
                #else
                // D.Err("Playing digital cancel twice this frame! Trace off");
                #endif
                return;
            }
            lastDigitalCancelFrame = f;
            #if TRACE_SOUNDS
            lastDigitalCancelTrace = Environment.StackTrace;
            #endif
            digitalCancelsLast = Rng.UFromExcept(digitalCancels, digitalCancelsLast);
            The.SysSig.PlayUISound.Send(digitalCancelsLast, clickVol);
        }
        public static void PlayMentalBreakdown() {
            if (G.IsDemoMode) { return; }
            var mode = A.S.Prefs.GetInt(Pref.EventSoundMode, PrefH.EventSoundMode, 0);
            if (mode > 0) { return; }
            Play("FX/MentalBreakdownSFX");
        }
        public static void Play(string sound, float vol = 1f) {
            // D.Warn("Playing sound: {0}", sound);
            lastEventSoundTime = GameTimer.realtimeSinceStartupAsDouble;
            The.SysSig.PlayUISound.Send(sound, vol);
        }

        // Event class sounds silence the fanfare beep for a moment, so that
        // events that already announce themselves don't play two sounds
        private static void SendEventSound(string sound, float vol) {
            lastEventSoundTime = GameTimer.realtimeSinceStartupAsDouble;
            The.SysSig.PlayUISound.Send(sound, vol);
        }

        public static void PlayFanfare() {
            var time = GameTimer.realtimeSinceStartupAsDouble;
            if (time - lastEventSoundTime < fanfareSilenceWindow) { return; }
            if (time - lastFanfareTime < fanfareMinInterval) { return; }
            lastFanfareTime = time;
            The.SysSig.PlayUISound.Send("UI/FanfareBeep", fanfareVol);
        }
        private static string digitalPopupsLast;
        private static readonly string[] digitalPopups = new [] {
            "UI/DigitalPopup_01",
            "UI/DigitalPopup_02",
            "UI/DigitalPopup_03",
            "UI/DigitalPopup_04",
            "UI/DigitalPopup_05",
        };

        private static string[] uiHoverSoundLast = new string[4];
        private static string[] uiClickDownSoundLast = new string[3];
        private static string[] uiClickUpSoundLast = new string[3];
        private static readonly string[][] uiHoverSounds = new string[][] {
            // Lowest
            new string[] {
                "UI/Hover1_1",
                "UI/Hover1_2",
                "UI/Hover1_3",
                "UI/Hover1_4",
            },
            // Low
            new string[] {
                "UI/Hover2_1",
                "UI/Hover2_2",
                "UI/Hover2_3",
                "UI/Hover2_4",
            },
            // Mid
            new string[] {
                "UI/Hover3_1",
                "UI/Hover3_2",
                "UI/Hover3_3",
                "UI/Hover3_4",
            },
            // High
            new string[] {
                "UI/Hover4_1",
                "UI/Hover4_2",
                "UI/Hover4_3",
                "UI/Hover4_4",
            },
        };
        private static readonly string[][] uiClickDownSounds = new string[][] {
            // Small
            new string[] {
                "UI/ClickDown1_1",
                "UI/ClickDown1_2",
                "UI/ClickDown1_3",
            },
            // Big
            new string[] {
                "UI/ClickDown2_1",
            },
            // High
            new string[] {
                "UI/ClickDown3_1",
                "UI/ClickDown3_2",
                "UI/ClickDown3_3",
                "UI/ClickDown3_4",
            },
        };
        private static readonly string[][] uiClickUpSounds = new string[][] {
            // Small
            new string[] {
                "UI/ClickUp1_1",
                "UI/ClickUp1_2",
                "UI/ClickUp1_3",
            },
            // Big
            new string[] {
                "UI/ClickUp2_1",
            },
            // High
            new string[] {
                "UI/ClickUp3_1",
                "UI/ClickUp3_2",
                "UI/ClickUp3_3",
                "UI/ClickUp3_4",
            },
        };

        private static string digitalLongsLast;
        private static readonly string[] digitalLongs = new [] {
            "UI/DigitalLong_01",
            "UI/DigitalLong_02",
            "UI/DigitalLong_03",
            "UI/DigitalLong_04",
            "UI/DigitalLong_05",
        };

        private static string digitalLongCancelsLast;
        private static readonly string[] digitalLongCancels = new [] {
            "UI/DigitalLongCancel_01",
            "UI/DigitalLongCancel_02",
            "UI/DigitalLongCancel_03",
            "UI/DigitalLongCancel_04",
            "UI/DigitalLongCancel_05",
        };

        private static string digitalConfirmsLast;
        private static readonly string[] digitalConfirms = new [] {
            "UI/DigitalConfirm_01",
            "UI/DigitalConfirm_02",
            "UI/DigitalConfirm_03",
            "UI/DigitalConfirm_04",
            "UI/DigitalConfirm_05",
        };

        private static string digitalCancelsLast;
        private static readonly string[] digitalCancels = new [] {
            "UI/DigitalCancel_01",
            "UI/DigitalCancel_02",
            "UI/DigitalCancel_03",
            "UI/DigitalCancel_04",
            "UI/DigitalCancel_05",
        };

        private static string dialogsLast;
        private static readonly string[] dialogs = new [] {
            "UI/Dialog_01",
            "UI/Dialog_02",
            "UI/Dialog_03",
            "UI/Dialog_04",
            "UI/Dialog_05",
            "UI/Dialog_06",
            "UI/Dialog_07",
        };

        public static void PlayResearchComplete() {
            SendEventSound("FX/ResearchComplete", eventVol);
        }

        private static bool isListeningToSettingsChange;

        public static void AddSettingsListener() {
            if (isListeningToSettingsChange) { return; }
            isListeningToSettingsChange = true;
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            OnSettingChanged(Pref.GAudioUIHoverSounds);
        }

        private static void OnSettingChanged(string setting) {
            if (setting == Pref.GAudioUIHoverSounds) {
                isUIhoverSounds = The.Prefs.GetBool(Pref.GAudioUIHoverSounds,
                    PrefH.GAudioUIHoverSounds, true);
            }
        }

        public static void PlayShowDialog() {
            dialogsLast = Rng.UFromExcept(dialogs, dialogsLast);
            The.SysSig.PlayUISound.Send(dialogsLast, dialogueVol);
        }

        private const float dialogTypeVol = 0.2f;
        private static string dialogTypesLast;
        private static readonly string[] dialogTypes = new [] {
            "UI/DialogType_01",
            "UI/DialogType_02",
            "UI/DialogType_03",
            "UI/DialogType_04",
            "UI/DialogType_05",
            "UI/DialogType_06",
        };

        public static void PlayDialogType() {
            dialogTypesLast = Rng.UFromExcept(dialogTypes, dialogTypesLast);
            var vol = dialogTypeVol * (0.85f + Rng.UValue * 0.15f);
            The.SysSig.PlayUISound.Send(dialogTypesLast, vol);
        }

        private static int lastEmergencyFrame;

        public static void PlayEventEmergency() {
            if (G.IsDemoMode) { return; }
            var f = GameTimer.frameCount;
            if (lastEmergencyFrame == f) {
                #if TRACE_SOUNDS
                D.Err("Playing emergency twice this frame! {0}", lastEmergencyTrace);
                #else
                // D.Err("Playing emergency twice this frame! Trace off");
                #endif
                return;
            }
            lastEmergencyFrame = f;
            #if TRACE_SOUNDS
            lastEmergencyTrace = Environment.StackTrace;
            #endif
            #if TRACE_SOUNDS
            D.ErrSoft("Playing Claxon2");
            #endif
            SendEventSound("UI/Claxon2", eventVol);
            The.SysSig.GamepadRumble.Send(0.2f, 0.3f, 0.2f);
        }
        private static double lastActionDeniedTime;
        public static void PlayActionDenied() {
            #if !PRODUCTION_BUILD
            D.Log("Booboop");
            #endif
            var time = GameTimer.realtimeSinceStartupAsDouble;
            if (time - lastActionDeniedTime < 0.25f) {
                #if TRACE_SOUNDS
                D.Err("Playing action denied twice this frame! {0}", lastActionDeniedTrace);
                #else
                // D.Err("Playing action denied twice this frame! Trace off");
                #endif
                return;
            }
            lastActionDeniedTime = time;
            #if TRACE_SOUNDS
            lastActionDeniedTrace = Environment.StackTrace;
            #endif
            // if (GameTimer.realtimeSinceStartup > nextPlayActionDeniedTime) {
            The.SysSig.PlayUISound.Send("UI/ActionDenied", deniedVol);
                // nextPlayActionDeniedTime = GameTimer.realtimeSinceStartup + 0.2f;
            // }
        }
        private static int lastEventWarningFrame;
        public static void PlayEventWarning() {
            if (G.IsDemoMode) { return; }
            var f = GameTimer.frameCount;
            if (lastEventWarningFrame == f) {
                #if TRACE_SOUNDS
                D.Err("Playing event warning twice this frame! {0}", lastEventWarningTrace);
                #else
                // D.Err("Playing event warning twice this frame! Trace off");
                #endif
                return;
            }
            lastEventWarningFrame = f;
            #if TRACE_SOUNDS
            lastEventWarningTrace = Environment.StackTrace;
            #endif
            SendEventSound("UI/SoftBeep", eventVol);
        }
        public static void PlayClockChange(ClockSpeed speed) {
            if (!Ready.Game || !Ready.Gen || UIShowing.LoadingScreen) { return; }
            string sound;
            switch (speed) {
                case ClockSpeed.Stopped: sound = "UI/ClockStop"; break;
                case ClockSpeed.Normal: sound = "UI/ClockNormal"; break;
                case ClockSpeed.Fast1: sound = "UI/ClockFaster"; break;
                case ClockSpeed.Fast2: sound = "UI/ClockFast"; break;
                default: sound = "UI/ClockFast"; break;
            }
            #if DEBUG_CLOCK_SOUNDS
            D.Warn("Playing clock sound: {0}", sound);
            #endif
            The.SysSig.PlayUISound.Send(sound, eventVol);
        }

        public static void PlayHyperjump() {
            Play("FX/HyperjumpSFX");
        }
    }
}
