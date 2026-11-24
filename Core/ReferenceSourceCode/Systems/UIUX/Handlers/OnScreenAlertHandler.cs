#if !PRODUCTION_BUILD
// #define DEBUG_FANFARE
#endif
using Game.Data;
using Game.UI;
using Game.Utils;
using KL.Clock;
using KL.Utils;
using UnityEngine;
using Uween;

namespace Game.Systems.UIUX {
    /// <summary>
    /// Manages on-screen fanfare and error messages
    /// </summary>
    public sealed class OnScreenAlertHandler : BaseWidgetHandler<OnScreenAlertWidget> {
        private readonly FanfareMessage fanfare;
        private readonly FanfareMessage errorMsg;
        private FanfareHandler fanfareManager;
        private FanfareHandler errorMsgManager;

        private const float fanfareTimeout = 10f;
        private const float errMsgTimeout = 3f;

        private Tween errorTween;
        private int fanfarePendingFrame = -1;

        public OnScreenAlertHandler(GameState s, OnScreenAlertWidget widget) : base(s, widget) {
            fanfare = widget.FanfareMessage;
            errorMsg = widget.ErrorMessage;

            fanfareManager = new FanfareHandler(fanfare, fanfareTimeout, 75);
            errorMsgManager = new FanfareHandler(errorMsg, errMsgTimeout, 100);

            fanfare.Hide();
            errorMsg.Hide();

            Subscribe();
        }

        public void Update() {
            // Nothing is announced over the new game intro. That includes what was queued
            // before it while the game was set up: entering the starting sector runs its
            // permanent effects, which announce themselves. Dropped, not held back for later -
            // their notification and log line stay
            if (S.Vars.IsShowingCutscene
                    && (fanfarePendingFrame >= 0 || fanfareManager.HasMessages)) {
                fanfarePendingFrame = -1;
                fanfareManager.Clear();
            }
            // Delayed by a frame, so that an event sound sent after the fanfare
            // within the same frame still silences the beep
            if (fanfarePendingFrame >= 0 && GameTimer.frameCount > fanfarePendingFrame) {
                fanfarePendingFrame = -1;
                if (!UIShowing.MainMenu) {
                    UISounds.PlayFanfare();
                }
            }
            #if DEBUG_FANFARE
            UpdateFanfares(0f);
            #else
            UpdateFanfares(GameTimer.unscaledDeltaTime);
            #endif
            UpdateErrorMsgs(GameTimer.unscaledDeltaTime);
        }

        public void Cleanup() {
            fanfarePendingFrame = -1;
            fanfareManager?.Clear();
            errorMsgManager?.Clear();
        }

        private void OnClearFanfare(FanfareMessage ffm) {
            if (ffm == fanfare) {
                fanfareManager?.Clear();
            } else if (ffm == errorMsg) {
                errorMsgManager?.Clear();
            }
        }

        private void OnShowFanfare(string msg) {
            // D.ErrSoft("Skipping fanfare display: {0}", msg);
            fanfarePendingFrame = GameTimer.frameCount;
            fanfareManager?.AddMessage(msg);
        }

        private void UpdateErrorMsgs(float dt) {
            errorMsgManager?.Update(dt);
        }

        // if the clock is fast, we will wait for real time 10 seconds to timeout
        // if the clock is paused, we will not timeout after 10 seconds in realtime
        // but instead we'll timeout after 10 seconds of simulation time
        private void UpdateFanfares(float dt) {
            fanfareManager?.Update(dt);
        }

        private void OnShowError(string msg) {
            UISounds.PlayActionDenied();
            errorMsgManager?.AddMessage(msg);
            if (errorTween?.IsComplete != false) {
                errorTween = MiscUtils.PunchScale(errorMsg.gameObject, 1.1f, 0.05f);
            }
        }

        private void Subscribe() {
            S.Sig.ShowFanfare.AddListener(OnShowFanfare);
            S.Sig.ShowErrorMessage.AddListener(OnShowError);
            S.Sig.ClearFanfare.AddListener(OnClearFanfare);
        }

        public void Unsubscribe() {
            if (S?.Sig != null) {
                S.Sig.ShowFanfare.RemoveListener(OnShowFanfare);
                S.Sig.ShowErrorMessage.RemoveListener(OnShowError);
                S.Sig.ClearFanfare.RemoveListener(OnClearFanfare);
            }
            Cleanup();
        }
    }
}
