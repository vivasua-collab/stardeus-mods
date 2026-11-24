using System;
using System.Collections.Generic;
using Game.Constants;
using Game.Data;
using Game.Data.Factions;
using Game.Input;
using Game.Systems.Diplomacy;
using Game.UI;
using Game.Utils;
using KL.Pool;
using KL.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Systems.UIUX {
    public sealed class GiftChoiceWidget : BaseWidget {
        [SerializeField] private WGroup container;
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private WText header;
        [SerializeField] private WText footerText;
        [SerializeField] private WText footerTextNoButton;
        [SerializeField] private WButtonText cancelButton;
        public bool IsShowing => gameObject.activeSelf;
        private Action<GiftPackage> onChoose;
        private Lifetime lifetime;
        public void Initialize() {
            Register(UIWidgetId.GiftChoice, this);
            container.DestroyChildren();
            Toggle(false);
        }

        public bool Show(GameState s, List<GiftPackage> options, Action<GiftPackage> onChoose) {
            if (IsShowing) { return true; }
            if (!s.UIQuery.ShowPanelModalFor.Ask(this, true)) {
                // another modal is showing!
                return false;
            }
            if (lifetime != null) {
                lifetime.Dispose();
            }
            lifetime = Lifetime.Create("GiftChoiceWidget");
            Toggle(true);
            UIUtils.CloseFullScreenUI();
            s.UIQuery.ToggleGameUI.Ask(false, false);
            scroll.verticalNormalizedPosition = 1f;

            string selectText = T.Select;
            header.Load(T.ChooseAPackage);
            GiftPackage cancelPackage = null;
            if (options.Count == 0) {
                D.Err("No options for gift choice!");
                return true;
            }
            var first = options[0];
            var faction = first.Faction;
            var attitude = s.Query.FactionAttitude.Ask(faction, FactionDef.Player);
            var pct = Units.PercentageSigned(attitude, 2, true);
            var facLine = T.FactionStandingTip_V2.F(faction.NameWithGlyph, pct);
            selectText = first.Direction == GiftDirection.PlayerToNPC
                ? T.OfferThisPackage
                : T.TakeThisPackage;
            cancelPackage = options.Find(p => p.IsCancel);

            var fText = LoadCancelButton(cancelPackage);
            fText.Load(facLine);

            var prefab = ResourceLibrary.FindPrefab<WGiftChoiceOption>(
                UIPrefabId.GiftChoiceOptionWidget);
            int idx = 0;
            this.onChoose = onChoose;
            foreach (var opt in options) {
                if (opt.IsCancel) {
                    continue;
                }
                var w = UISpawn.Widget(lifetime, prefab, container.Container, idx++);
                w.Load(lifetime, s, opt, selectText, OnChoose);
            }
            return true;
        }

        private WText LoadCancelButton(GiftPackage cancelPackage) {
            if (cancelPackage == null) {
                cancelButton.SetActiveFast(false);
                footerText.SetActiveFast(false);
                footerTextNoButton.SetActiveFast(true);
                return footerTextNoButton;
            }

            cancelButton.SetActiveFast(true);
            footerText.SetActiveFast(true);
            footerTextNoButton.SetActiveFast(false);
            cancelButton.Load(T.CancelTransaction, () => OnChoose(cancelPackage));

            UIUtils.AddTip(lifetime, cancelButton, GiftConsequenceTip.ForCancel(cancelPackage));
            return footerText;
        }

        private void OnChoose(GiftPackage data) {
            onChoose.Invoke(data);
            UISounds.PlayDigitalConfirm();
            Toggle(false);
            if (lifetime != null) {
                lifetime.Dispose();
                lifetime = null;
            }
            UIUtils.CloseFullScreenUI();
            A.S.UIQuery.ToggleGameUI.Ask(true, false);
            A.S.UIQuery.ShowPanelModalFor.Ask(this, false);
        }
    }
}
