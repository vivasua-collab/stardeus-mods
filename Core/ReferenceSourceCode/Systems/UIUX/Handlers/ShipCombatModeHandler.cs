using System;
using Game.CodeGen;
using Game.Constants;
using Game.Data;
using Game.Input;
using Game.Systems.Ships;
using Game.UI;
using KL.Utils;
using UnityEngine.InputSystem;

namespace Game.Systems.UIUX {
    public sealed class ShipCombatModeHandler : BaseWidgetHandler<ShipCombatMiniWidget> {
        private ShipCombatOurWidget ourWidget;
        private ShipCombatTheirWidget theirWidget;
        private ShipCombatStationsWidget stationsWidget;
        private readonly ShipCombatViewHint viewHint;
        public ShipCombatModeHandler(GameState s,
                    ShipCombatMiniWidget widget,
                    ShipCombatOurWidget ourWidget,
                    ShipCombatTheirWidget theirWidget,
                    ShipCombatStationsWidget stationsWidget
                ) : base(s, widget) {
            this.ourWidget = ourWidget;
            this.theirWidget = theirWidget;
            this.stationsWidget = stationsWidget;
            widget.Initialize(s);
            ourWidget.Initialize(s);
            theirWidget.Initialize(s);
            stationsWidget.Initialize(s);
            viewHint = new ShipCombatViewHint(widget.ViewArrowRect);
            s.UIQuery.ToggleShipCombatMini.SetResponder(OnToggleMini);
            s.UIQuery.ToggleShipCombat.SetResponder(OnToggleCombat);
            s.UISig.ToggleShipCombatMode.AddListener(OnToggleShipCombatMode);
            s.Sig.ShipCombatChanged.AddListener(OnShipCombatChanged);
            s.Sig.BattleStationChanged.AddListener(OnBattleStationChanged);
            s.Clock.OnTick.AddListener(OnTick);
            s.Sig.EveryFrame.AddListener(EveryFrame);
            The.SysSig.UIThemeReloaded.AddListener(OnUIThemeReloaded);
        }

        private bool OnToggleCombat(bool yes) {
            if (yes == UIShowing.ShipCombatMode) {
                return false;
            }
            UIShowing.Set(UIShowingFlags.ShipCombatMode, yes);
            S.UISig.ToggleShipCombatMode.Send(yes);
            return yes;
        }

        private void OnBattleStationChanged(BattleStationComp station) {
            if (UIShowing.ShipCombatMini) {
                widget.RebuildBattleStations();
            }
            if (UIShowing.ShipCombatMode) {
                stationsWidget.RebuildBattleStations();
            }
        }

        private void OnToggleShipCombatMode(bool on) {
            if (on) {
                viewHint.MarkOpened();
                if (!The.Toolbox.IsFreeSelect) {
                    The.Toolbox.Clear();
                }
                S.Query.DeselectAny.Ask();
            } else {
                ResetSimulatedShortcutHolds();
            }
            var enemies = S.Query.GetEnemyShips.Ask();
            ourWidget.Toggle(on);
            theirWidget.Toggle(on && enemies.Count > 0);
            stationsWidget.Toggle(on);
            RefreshMiniVisibility();
        }

        private void EveryFrame() {
            viewHint.Update();
            if (!Ready.Input || !Ready.Game || The.Bindings.IsLocked || !UIShowing.ShipCombatMode) {
                ResetSimulatedShortcutHolds();
                return;
            }
            ourWidget.SimulateVentHold(The.Bindings.IsPressed(ActionType.ShipCombatToggleVent));
            var k = Keyboard.current;
            if (k == null || k.shiftKey.isPressed || k.ctrlKey.isPressed
                    || The.Bindings.IsPressed(ActionType.AltModifier)) {
                SimulateStationFireHolds(false, false, false, false, false,
                    false, false, false, false, false);
                return;
            }
            SimulateStationFireHolds(
                k.f1Key.isPressed,
                k.f2Key.isPressed,
                k.f3Key.isPressed,
                k.f4Key.isPressed,
                k.f5Key.isPressed,
                k.f6Key.isPressed,
                k.f7Key.isPressed,
                k.f8Key.isPressed,
                k.f9Key.isPressed,
                k.f10Key.isPressed);
        }

        private void SimulateStationFireHolds(bool f1, bool f2, bool f3, bool f4, bool f5,
                bool f6, bool f7, bool f8, bool f9, bool f10) {
            stationsWidget.SimulateFireHold(0, f1);
            stationsWidget.SimulateFireHold(1, f2);
            stationsWidget.SimulateFireHold(2, f3);
            stationsWidget.SimulateFireHold(3, f4);
            stationsWidget.SimulateFireHold(4, f5);
            stationsWidget.SimulateFireHold(5, f6);
            stationsWidget.SimulateFireHold(6, f7);
            stationsWidget.SimulateFireHold(7, f8);
            stationsWidget.SimulateFireHold(8, f9);
            stationsWidget.SimulateFireHold(9, f10);
        }

        private void ResetSimulatedShortcutHolds() {
            ourWidget.ResetSimulatedShortcutHolds();
            stationsWidget.ResetSimulatedFireHolds();
        }

        public void OnTick(long ticks) {
            RefreshMiniVisibility();
            if (UIShowing.ShipCombatMini) {
                widget.UpdateBattleStations();
            }
            if (UIShowing.ShipCombatMode) {
                ourWidget.OnTick();
                theirWidget.OnTick();
                stationsWidget.UpdateBattleStations();
            }
        }

        private bool OnToggleMini(bool show) {
            return RefreshMiniVisibility();
        }

        private void OnShipCombatChanged(bool on) {
            RefreshMiniVisibility();
        }

        private bool RefreshMiniVisibility() {
            bool showMini = ShouldShowMini();
            if (UIShowing.ShipCombatMini != showMini) {
                widget.Toggle(showMini);
            } else if (showMini) {
                widget.RefreshHeader();
            }
            return UIShowing.ShipCombatMini;
        }

        private void ShouldShowMiniFallback() {
            if (A.Player?.Ship == null) {
                D.Err("Ship combat mini widget show callback failed to work, player or ship still null!");
                return;
            }
            D.Warn("Showing combat mini widget in fallback");
            ShouldShowMini();
        }

        private bool ShouldShowMini() {
            if (UIShowing.ShipCombatMode) {
                return false;
            }
            var playerShip = A.Player?.Ship;
            if (playerShip == null) {
                // we are likely trying to show this too early right after
                // load, for example if we have security threats.
                // Let's recheck after everything is loaded
                Ready.When(WhenH.ShipDataRestored, ShouldShowMiniFallback);
                return false;
            }
            var flux = playerShip.CombatState.Flux;
            var enemies = S.Query.GetEnemyShips.Ask();
            return playerShip.IsInCombat
                || enemies.Count > 0
                || flux.Hard > 0f
                || flux.Soft > 0f
                || S.Sys.Sec.HasActiveSpaceThreats;
        }

        public void Cleanup() {
            ResetSimulatedShortcutHolds();
            S.Sig.ShipCombatChanged.RemoveListener(OnShipCombatChanged);
            S.Sig.EveryFrame.RemoveListener(EveryFrame);
            The.SysSig.UIThemeReloaded.RemoveListener(OnUIThemeReloaded);
        }

        private void OnUIThemeReloaded() {
            widget.SetColors();
            ourWidget.SetColors();
            theirWidget.SetColors();
        }
    }
}
