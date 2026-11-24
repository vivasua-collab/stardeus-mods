using System;
using Game.Constants;
using Game.Procgen;
using Game.Utils;
using KL.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Uween;

namespace Game.UI {
    public sealed class UICountdownWidget : MonoBehaviour {
        [SerializeField] private TMP_Text text;
        [SerializeField] private Slider progress;

        public long StartTick;
        public long EndTick;
        public bool IsSilent;
        private Action onEnd;
        private Action onTick;
        private bool isActive;

        public void Setup(long startTick, int duration, Action onEnd, Action onTick) {
            if (duration < 1) {
                D.Err("Duration < 1 in countdown widget! {0}", duration);
                Reset();
                return;
            }
            this.StartTick = startTick;
            this.EndTick = startTick + duration;
            this.onEnd = onEnd;
            this.onTick = onTick;
            this.IsSilent = false;
            isActive = true;
            Tick(startTick);
            gameObject.SetActiveFast(true);
        }
        public void Reset() {
            StartTick = 0L;
            EndTick = 0L;
            onEnd = null;
            onTick = null;
            IsSilent = false;
            isActive = false;
        }

        private string lastText = ".";

        /// <returns>true if countdown was reached</returns>
        public bool Tick(long ticks) {
            if (!isActive) {
                // nothing to do
                return false;
            }
            onTick?.Invoke();
            var total = EndTick - StartTick;
            var current = ticks - StartTick;
            progress.value = Mathf.Clamp01(1f - current / (float) total);
            var left = EndTick - ticks;
            if (left >= 0L) {
                var txt = Units.TicksToTime(left, true);
                if (txt != lastText) {
                    lastText = txt;
                    text.SetText(Texts.Accented(txt, false));
                    text.transform.localScale = Consts.V3One;
                    switch (G.ClockSpeed) {
                        case ClockSpeed.Normal: {
                                PunchClock();
                                break;
                            }
                        case ClockSpeed.Fast1: {
                            if (G.Ticks % Consts.TicksPer3Minutes == 0) {
                                PunchClock();
                            }
                            break;
                        }
                        case ClockSpeed.Fast2: {
                            if (G.Ticks % Consts.TicksPer10Minutes == 0) {
                                PunchClock();
                            }
                            break;
                        }
                    }
                }
                return false;
            } else {
                // give extra few minutes in game time to do the click
                if (left < -2) {
                    onEnd?.Invoke();
                    return true;
                }
                return false;
            }
        }

        private void PunchClock() {
            if (!G.HasEnteredGame) { return; }
            MiscUtils.PunchScale(text.gameObject, amt: 1.1f);
            if (IsSilent) { return; }
            UISounds.PlayClockChange(ClockSpeed.Stopped);
        }

        public void Hide() {
            gameObject.SetActiveFast(false);
            Reset();
        }
    }
}
