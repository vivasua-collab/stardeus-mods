using System;
using Game.Constants;
using Game.Data;
using Game.Data.Economy;
using Game.Utils;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class TopBarWidget : BaseWidget {
        [SerializeField] private ElectricityMiniChartWidget electricityWidget;
        [SerializeField] private WButtonIconText btnWallet;
        public RectTransform ElectricityRect => electricityWidget.Rect;
        public RectTransform WalletRect => btnWallet.Rect;
        public void Initialize() {
            electricityWidget.SetColors();
            Register(UIWidgetId.TopBar, this);
            btnWallet.SetImage(IconId.CStarcreds);
        }


        public void RigWalletButton(Lifetime life, Wallet wallet, Action onClick) {
            btnWallet.SetOnClick(onClick);
            wallet.AmountB.Bind(life, UpdateWallet);
        }
        public void RigElectricityButton(Action onClick) {
            electricityWidget.SetOnClick(onClick);
        }

        private void UpdateWallet(double amt) {
            if (btnWallet.SetText(Wallet.ToValueString(amt, withSymbol: false))) {
                btnWallet.RebuildLayout();
            }
        }

        public void UpdateElectricity(Situation sit) {
            electricityWidget.UpdateFull(sit);
        }

        public void RingElectricityDeficit(bool focus) {
            electricityWidget.RingDeficit(focus);
        }

        public void SetupElectricityChart(GameState s) {
            electricityWidget.Setup(s);
        }

        public void ThemeReloaded() {
            electricityWidget.SetColors();
            electricityWidget.UpdateFull(A.S.Situation);
        }
    }
}
