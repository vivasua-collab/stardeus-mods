using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    [DisallowMultipleComponent]
    public class WVentControls : WBase {
        [SerializeField] private WImage wImage;
        [SerializeField] private WButtonTextLongPress wButton;
        [SerializeField] private WText wTextLabel;
        [SerializeField] private WText wTextState;

        private static readonly string[] OpenFrames = {
            GraphicsId.ObjectsDeviceAnimsFluxVent_Anim_F0,
            GraphicsId.ObjectsDeviceAnimsFluxVent_Anim_F1,
            GraphicsId.ObjectsDeviceAnimsFluxVent_Anim_F2,
        };

        private bool isVenting;
        private bool disableButtonPending;
        private ShipFluxState disableButtonPendingState;
        private float disableButtonPendingUntil;
        private ShipFlux flux;
        private GameState S;
        private const float DisableButtonFallbackSeconds = 1f;

        public void Initialize(GameState s) {
            S = s;
            wTextLabel.Load(T.NounFluxVentState);
            wButton.Load(VentButtonText(false), Tunables.ShipCombatManualFireHoldDuration,
                OnToggleVent, OnHoldButton);
        }

        public void Load(Lifetime lifetime, ShipFlux flux) {
            this.flux = flux;
            disableButtonPending = false;
            UpdateFromFlux(flux);
            flux.OnChanged.Bind(lifetime, () => UpdateFromFlux(flux));
        }

        public void SimulateHold(bool isDown) {
            wButton.SimulateHold(isDown);
        }

        public void ResetSimulatedHold() {
            wButton.ResetSimulatedHold();
        }

        private void Update() {
            if (isVenting) {
                int frame = Mathf.FloorToInt(Time.time * 15f) % 3;
                wImage.SetImage(OpenFrames[frame]);
            }
            if (flux != null && GameState.IsCurrent(S)) {
                var shieldAnimating = S.Query.IsPlayerShieldAnimating.Ask();
                wButton.SetInteractable(!flux.IsOverloaded && !shieldAnimating &&
                    !IsDisableButtonPending(shieldAnimating));
            }
        }

        private bool IsDisableButtonPending(bool shieldAnimating) {
            if (!disableButtonPending) {
                return false;
            }
            if (shieldAnimating || flux.State != disableButtonPendingState ||
                    GameTimer.unscaledTime >= disableButtonPendingUntil) {
                disableButtonPending = false;
                return false;
            }
            return true;
        }

        private void UpdateFromFlux(ShipFlux flux) {
            isVenting = flux.IsVenting;
            if (flux.IsOverloaded) {
                wTextState.SetText(Texts.Red(T.FluxOverload));
            } else if (flux.IsVenting) {
                wTextState.SetText(Texts.Accented(T.VentOpen));
            } else {
                wTextState.SetText(T.VentClosed);
                wImage.SetImage(GraphicsId.ObjectsDevicesFluxVent_Closed);
            }
            wButton.SetText(VentButtonText(flux.IsVenting));
        }

        private string VentButtonText(bool isVenting) {
            return Texts.TitleWithShortcutGlyph(
                isVenting ? T.CloseVent : T.OpenVent,
                ActionType.ShipCombatToggleVent);
        }

        private void OnHoldButton(float pct) {
            if (G.ClockSpeed != ClockSpeed.Normal) {
                A.S.Clock.SetSpeed(ClockSpeed.Normal);
            }
        }

        private void OnToggleVent() {
            var cs = A.Player.Ship.CombatState;
            var f = cs.Flux;
            if (f.ToggleVent()) {
                cs.FluxSettings.IsVentingManuallyNow = f.State == ShipFluxState.Venting;
                UpdateFromFlux(f);
                disableButtonPending = true;
                disableButtonPendingState = f.State;
                disableButtonPendingUntil = GameTimer.unscaledTime + DisableButtonFallbackSeconds;
                wButton.SetInteractable(false);
            } else {
                disableButtonPending = false;
                UISounds.PlayActionDenied();
            }
        }
    }
}
