using UnityEngine;
using Game.Commands;
using KL.Clock;
using System.Collections;
using UnityEngine.UI;
using KL.Utils;
using Game.Data;
using KL.UI;
using System;
using Game.Utils;
using Game.CodeGen;
using Game.Constants;
using System.Diagnostics;
using Game.Input;

namespace Game.UI {
    public class SetGameSpeedButton : CommandButton {
        [SerializeField] private ClockSpeed speed;
        [SerializeField] private Image image;
        [SerializeField] private Color activeColor;
        [SerializeField] private Color inactiveColor;
        [SerializeField] private Color disabledColor;
        [SerializeField] private string activeColorId;
        [SerializeField] private string inactiveColorId;
        [SerializeField] private string disabledColorId;
        private Color forceStopColor;

        private bool isDisabled;

        private bool isSubscribed;
        private GameState S;
        private UIHoverCallback hoverCallback;
        private string speedTooltip;
        public ClockSpeed Speed => speed;

        public IEnumerator Start() {
            if (TryGetComponent<UIHoverCallback>(out var hover)) {
                hoverCallback = hover;
            } else {
                hoverCallback = gameObject.AddComponent<UIHoverCallback>();
            }
            hoverCallback.SetCallback(OnHover, null);
            S = The.R.State;
            var curS = S;
            if (speed < S.Clock.MinSpeed) {
                isDisabled = true;
                button.interactable = false;
                yield break;
            }
            if (speed > ClockSpeed.Fast2) {
                isDisabled = true;
                button.interactable = false;
                yield break;
            }
            while (!Ready.Core) { yield return null; }
            // D.Warn("active color: {0}", activeColor);
            if (!GameState.IsCurrent(curS)) { yield break; }
            if (curS.Clock == null) { yield break; }
            LoadColors();
            // D.Warn("Inactive color: {0}", activeColor);
            isSubscribed = true;
            The.SysSig.UIThemeReloaded.AddListener(ReloadTheme, gameObject);
            curS.Clock.OnSpeedChange.AddListener(OnSpeedChange, gameObject);
            curS.Clock.OnRestrictionChange.AddListener(OnRestrictionChange, gameObject);
            if (speed == ClockSpeed.Stopped) {
                curS.Clock.OnForceStopChange.AddListener(OnForceStopChange, gameObject);
            }
            OnSpeedChange(curS.Clock.Speed);
            // speedTooltip = T.GameSpeedMultiplier_V1.F(S.Clock.GetSpeedMultiplierForUI(speed));
            var act = ActForSpeed();
            if (The.Bindings.HasBinding(act)) {
                var glyph = The.Bindings.GetBinding(act).AllControlGlyphs(out var tip);
                speedTooltip = T.ShortcutTip_V1.F(glyph);
                // speedTooltip = $"{speedTooltip}\n{T.ShortcutTip_V1.F(glyph)}";
            }
        }

        private void OnForceStopChange(bool isStopped) {
            if (isStopped) {
                image.color = forceStopColor;
            } else {
                OnSpeedChange(S.Clock.Speed);
            }
        }

        private void LoadColors() {
            var theme = The.UITheme;
            activeColor = theme.GetColor(activeColorId, this);
            inactiveColor = theme.GetColor(inactiveColorId, this);
            disabledColor = theme.GetColor(disabledColorId, this);
            forceStopColor = theme.GetColor("Red", this);
        }

        private void ReloadTheme() {
            LoadColors();
            OnSpeedChange(S.Clock.Speed);
        }

        private ActionType ActForSpeed() {
            switch (speed) {
                case ClockSpeed.Stopped: return ActionType.ToggleTime;
                case ClockSpeed.Normal: return ActionType.TimeSpeedNormal;
                case ClockSpeed.Fast1: return ActionType.TimeSpeedFast1;
                case ClockSpeed.Fast2: return ActionType.TimeSpeedFast2;
                default: return ActionType.None;
            }
        }

        private void OnHover(bool over) {
            if (speedTooltip == null) { return; }
            if (over) {
                The.SysSig.ShowTooltip.Send(speedTooltip, true);
            } else {
                The.SysSig.HideTooltip.Send();
            }
        }

        private void OnDestroy() {
            if (isSubscribed) {
                The.SysSig.UIThemeReloaded.RemoveListener(ReloadTheme);
            }
        }

        private void OnRestrictionChange(ClockSpeed speed) {
            var wasDisabled = isDisabled;
            isDisabled = this.speed > speed;
            button.interactable = !isDisabled;
            if (!wasDisabled && isDisabled) {
                image.color = disabledColor;
            } else {
                // This will set the color back
                OnSpeedChange(G.ClockSpeed);
            }
        }

        private void OnSpeedChange(ClockSpeed newSpeed) {
            if (isDisabled) { return; }
            if (S.Clock.IsForceStopped && speed == ClockSpeed.Stopped) {
                image.color = forceStopColor;
                return;
            }
            if (image == null) {
                // D.Log("Image is null on speed button for speed {0}", newSpeed);
                return;
            }
            if (speed == newSpeed) {
                image.color = activeColor;
            } else {
                image.color = inactiveColor;
            }
        }

        protected override void OnClick() {
            var prevSpeed = S.Clock.SetSpeed(speed);
            UISounds.PlayClockChange(S.Clock.Speed);
            CheckSpecialMode(prevSpeed);
        }

        [Conditional("UNITY_EDITOR")]
        private void CheckSpecialMode(ClockSpeed prevSpeed) {
            if (prevSpeed != ClockSpeed.Stopped) { return; }
            if (The.Bindings.IsPressed(Input.ActionType.CtrlModifier)) {
                D.Warn("CTRL was pressed when clicking on Play, will run 1 tick. Frame: {0}. Tick: {1}",
                    Time.frameCount, S.Clock.Ticks);
                S.Clock.OnNextTick(() => {
                    S.Clock.SetSpeed(ClockSpeed.Stopped);
                    D.Warn("Stopped clock after one tick. Frame: {0}. Tick: {1}",
                        Time.frameCount, S.Clock.Ticks);
                });
            } else if (The.Bindings.IsPressed(Input.ActionType.ShiftModifier)) {
                D.Warn("Shift was pressed when clicking on Play, clock will run for 1 frame. Frame {0}. Tick: {1} ({2:0.0000})",
                    Time.frameCount, S.Clock.Ticks, S.Clock.TickProgress);
                Coroutines.StartDirect(StopClock());
            }
        }

        private IEnumerator StopClock() {
            yield return null;
            S.Clock.SetSpeed(ClockSpeed.Stopped);
            D.Warn("Stopping clock after 1 frame. Frame: {0}. Tick: {1} ({2:0.0000})",
                Time.frameCount, S.Clock.Ticks, S.Clock.TickProgress);
        }

        public override string ToString() {
            return $"SetGameSpeedButton[{speed}]";
        }
    }
}
