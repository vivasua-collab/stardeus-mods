using System;
using Game.Constants;
using Game.Data;
using Game.Systems.Notify;
using Game.UI;
using Game.Utils;

namespace Game.Systems.UIUX {
    public sealed class TopBarHandler : BaseWidgetHandler<TopBarWidget> {
        private Lifetime topBarLife;
        // Here rather than on the widget, which outlives a game load
        private readonly ElectricityDeficitRing deficitRing = new();

        public TopBarHandler(GameState s, TopBarWidget widget) : base(s, widget) {
            topBarLife = Lifetime.Create("TopBar");
            widget.Initialize();
            Ready.WhenGame(this, "InitTopBar", LoadWidget);
            s.Sig.SituationUpdated.AddListener(OnSituationUpdate);
            The.SysSig.SettingChanged.AddListener(OnSettingChanged);
            The.SysSig.UIThemeReloaded.AddListener(OnThemeReload);
        }

        private void OnSituationUpdate(Situation sit) {
            widget.UpdateElectricity(sit);
            var kind = deficitRing.Next(sit.GridDeficit > 0f,
                GameTimer.realtimeSinceStartupAsDouble);
            if (kind == ElectricityDeficitRing.Kind.None) { return; }
            // The Electricity window already shows the deficit. A muted Alert type is one the
            // player asked not to be interrupted by, the same as for the hub's ring
            if (UIShowing.Electricity || NotificationMutes.IsMuted(NotificationType.Alert)) {
                return;
            }
            widget.RingElectricityDeficit(kind == ElectricityDeficitRing.Kind.Focus);
        }

        private void OnThemeReload() {
            widget.ThemeReloaded();
        }
        private void LoadWidget() {
            widget.RigElectricityButton(ToggleElectricity);
            widget.RigWalletButton(topBarLife, S.Wallet, ToggleWallet);
            widget.SetupElectricityChart(S);
        }

        private void ToggleWallet() {
            S.UIQuery.ToggleWallet.Ask(!UIShowing.Wallet);
        }

        private void ToggleElectricity() {
            S.UIQuery.ToggleElectricity.Ask(!UIShowing.Electricity);
        }

        public void Cleanup() {
            The.SysSig.SettingChanged.RemoveListener(OnSettingChanged);
            The.SysSig.UIThemeReloaded.RemoveListener(OnThemeReload);
            topBarLife.Dispose();
        }

        private void OnSettingChanged(string setting) {
            if (setting == Pref.GShowElectricityMiniChart) {
                widget.SetupElectricityChart(S);
            }
        }
    }
}
