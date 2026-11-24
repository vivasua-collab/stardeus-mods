using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Systems.Ships;
using Game.UI;
using Game.Utils;
using KL.Utils;
using UnityEngine;

namespace Game.Systems.UIUX {
    public sealed class ShipCombatOurWidget : BaseWidget {

        [SerializeField] private WImage wFactionIcon;
        [SerializeField] private WButton wButtonClose;
        [SerializeField] private WText wTextHeader;
        [SerializeField] private WText wTextOurShip;
        [SerializeField] private WText2 wTextClassPoints;
        [SerializeField] private WProgressText2Stacked wProgressFlux;
        [SerializeField] private WProgressText2 wProgressShield;
        [SerializeField] private WText2 wTextThrusters;
        [SerializeField] private WText2 wTextFluxCapacitors;
        [SerializeField] private WText2 wTextFluxVents;
        [SerializeField] private WGroup wButtonGroup;
        [SerializeField] private WVentControls wVentControls;
        private GameState S;
        private Lifetime lifetime;
        private readonly BFloat evadePct = new();
        private ShipCombatWidgetUtils.FluxVentStats fluxVentStats;
        private bool hasFluxVentStats;

        public void Initialize(GameState s) {
            gameObject.SetActiveFast(false);
            Register(UIWidgetId.ShipCombatOur, this);
            wButtonClose.Load(CloseCombatMode);
            wFactionIcon.Load(FactionDef.Player.FlagIcon);
            wTextOurShip.Load(T.OurShip);
            wVentControls.Initialize(s);
            SetColors();
            S = s;
        }

        private void CloseCombatMode() {
            S.UIQuery.ToggleShipCombat.Ask(false);
        }

        public override void Toggle(bool show) {
            base.Toggle(show);
            if (show) {
                Load();
                Rect.SetAsLastSibling();
            } else {
                Cleanup();
            }
        }

        public void Load() {
            Cleanup();
            lifetime = Lifetime.Create("ShipCombatOur");
            var ship = A.Player.Ship;
            wTextHeader.Load($"{ship.Class.NameT} {ship.NameWithTag}");

            UIUtils.AddTipFunc(lifetime, wProgressFlux, ShipCombatMiniWidget.ShowFluxTooltip);
            var flux = ship.CombatState.Flux;
            flux.OnChanged.Bind(lifetime, () => {
                var fluxLabel = flux.State == ShipFluxState.Overload ? T.FluxOverload : T.NounFlux;
                wProgressFlux.Progress.Load(fluxLabel,
                    Units.XOutOfY(flux.Hard + flux.Soft, flux.Capacity), flux.TotalPercent);
                wProgressFlux.SetStackedProgress(flux.HardPercent);
                UpdateFluxVents(ship);
            });

            wVentControls.Load(lifetime, flux);

            var configBtn = UISpawn.Widget<WBottomBarButton>(lifetime,
                UIPrefabId.WBottomBarButton, wButtonGroup.Container, 0);
            configBtn.Load(IconId.CSpaceShip, T.ShipConfigShort,
                () => A.S.UIQuery.ToggleShipConfig.Ask(!UIShowing.ShipConfig));

            UIUtils.AddTipFunc(lifetime, wTextClassPoints, () => ShipCombatWidgetUtils.GetClassPointsTip(ship));
            UIUtils.AddTipFunc(lifetime, wTextThrusters, () => ShipCombatWidgetUtils.GetThrustersTip(ship));
            UIUtils.AddTipFunc(lifetime, wTextFluxCapacitors,
                () => ShipCombatWidgetUtils.GetFluxCapacitorsTip(ship));
            UIUtils.AddTipFunc(lifetime, wTextFluxVents, () => ShipCombatWidgetUtils.GetFluxVentsTip(ship));

            UpdateClassPoints(ship);
            UpdateShield(ship);
            UpdateThrusters(ship);
            UpdateCapacitors(ship);
            evadePct.SetSilent(ship.CombatState.EvasionPercent);
            evadePct.Bind(lifetime, _ => UpdateThrusters(A.Player.Ship), notifyImmediately: false);

            S.Sig.ShipDevicePhaseChanged.AddListener(OnDevicePhaseChanged, gameObject);
            RebuildLayout();
        }

        public void SimulateVentHold(bool isDown) {
            wVentControls.SimulateHold(isDown);
        }

        public void ResetSimulatedShortcutHolds() {
            wVentControls.ResetSimulatedHold();
        }

        public void OnTick() {
            if (lifetime == null) { return; }
            var ship = A.Player.Ship;
            var shields = ship.CombatState.Shields;
            for (int i = shields.Count - 1; i >= 0; i--) {
                if (shields[i].Phase == ShipShieldPhase.Charging) {
                    UpdateShield(ship);
                    break;
                }
            }
            evadePct.Set(ship.CombatState.EvasionPercent);
        }

        private void OnDevicePhaseChanged(IShipDevice device) {
            if (device.Device.Ship != A.Player.Ship) { return; }
            var ship = A.Player.Ship;
            if (device is ShipCapacitor) {
                UpdateCapacitors(ship);
            } else if (device is ShipShield) {
                UpdateShield(ship);
            } else if (device is ShipEngine) {
                UpdateThrusters(ship);
            } else if (device is ShipVent) {
                UpdateFluxVents(ship);
            }
            UpdateClassPoints(ship);
        }

        private void UpdateClassPoints(NPCShip ship) {
            var cp = ship.CombatState.CurrentClassPoints;
            var mp = ship.Class.MaxClassPoints;
            var cpName = cp > mp ? Texts.Red(T.NounClassPoints) : T.NounClassPoints;
            wTextClassPoints.Load(cpName, Units.XOutOfY(cp, mp));
        }

        private void UpdateShield(NPCShip ship) {
            var shields = ship.CombatState.Shields;
            var total = shields.Count;
            if (total == 0) {
                wProgressShield.Load(T.NounShields, T.NotAvailable);
                return;
            }
            int up = 0;
            float bestChargePct = 0f;
            for (int i = shields.Count - 1; i >= 0; i--) {
                var s = shields[i];
                if (s.Phase == ShipShieldPhase.Active) {
                    up++;
                } else if (s.Phase == ShipShieldPhase.Charging && s.PhasePercent > bestChargePct) {
                    bestChargePct = s.PhasePercent;
                }
            }
            float pct;
            string text;
            if (up > 0) {
                pct = up / (float) total;
                text = up == total ? T.Active : Units.XOutOfY(up, total);
            } else if (bestChargePct > 0f) {
                pct = bestChargePct;
                text = Units.Percentage(bestChargePct);
            } else {
                pct = 0f;
                text = Units.XOutOfY(0, total);
            }
            wProgressShield.Load(T.NounShields, text, pct);
        }

        private void UpdateThrusters(NPCShip ship) {
            var thrCnt = ship.CombatState.NumThrustersActiveTotal;
            wTextThrusters.Load(
                $"{T.NounThrusters}: {Units.XOutOfY(thrCnt.Item1, thrCnt.Item2)}",
                $"{T.NounEvade}: {Units.Percentage(ship.CombatState.EvasionPercent)}");
        }

        private void UpdateCapacitors(NPCShip ship) {
            var stats = ShipCombatWidgetUtils.GetFluxCapacitorStats(ship);
            if (stats.Total > 0) {
                wTextFluxCapacitors.Load(
                    $"{T.NounFluxCapacitors}: {Units.XOutOfY(stats.Active, stats.Total)}",
                    ShipCombatWidgetUtils.GetFluxCapacitorBonusText(stats));
            } else {
                wTextFluxCapacitors.Load(T.NounFluxCapacitors, T.NotAvailable);
            }
        }

        private void UpdateFluxVents(NPCShip ship) {
            var stats = ShipCombatWidgetUtils.GetFluxVentStats(ship);
            if (hasFluxVentStats
                    && ShipCombatWidgetUtils.SameFluxVentStats(stats, fluxVentStats)) {
                return;
            }
            hasFluxVentStats = true;
            fluxVentStats = stats;
            if (stats.Total == 0) {
                wTextFluxVents.Load(T.NounFluxVents, T.NotAvailable);
                return;
            }
            wTextFluxVents.Load(
                $"{T.NounFluxVents}: {Units.XOutOfY(stats.Active, stats.Total)}",
                ShipCombatWidgetUtils.GetFluxVentBonusText(stats));
        }

        private void Cleanup() {
            ResetSimulatedShortcutHolds();
            if (lifetime != null) {
                S.Sig.ShipDevicePhaseChanged.RemoveListener(OnDevicePhaseChanged);
                lifetime.Dispose();
                lifetime = null;
            }
            hasFluxVentStats = false;
        }

        public void SetColors() {
            var fillColor = The.UITheme.GetColor("ShipCombatFluxSoft", "ShipCombatMini");
            var fill2Color = The.UITheme.GetColor("ShipCombatFluxHard", "ShipCombatMini");
            var shieldColor = The.UITheme.GetColor("ShipCombatShields", "ShipCombatMini");

            wProgressFlux.Progress.SetFillColor(fillColor);
            wProgressFlux.SetStackedColor(fill2Color);
            wProgressShield.SetFillColor(shieldColor);
        }
    }
}
